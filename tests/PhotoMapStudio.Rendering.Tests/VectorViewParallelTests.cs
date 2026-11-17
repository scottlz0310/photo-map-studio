using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

public class VectorViewParallelTests
{
    private const int BaseZoom = 14;
    private const int TileX = 14370;
    private const int TileY = 6488;

    private static readonly (int X, int Y)[] Winding =
    [
        (100, 3900), (900, 2100), (2100, 2500), (3200, 600), (3900, 1500),
    ];

    private static readonly (int X, int Y)[] Straight = [(0, 1800), (4096, 2300)];

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task 帯の並列描画は面だけなら逐次と同じ画素になる(int bands)
    {
        byte[] tile = MvtTestTile.CreateWater(rightEdge: 3072);
        using var style = new OpenMapTilesStyle();

        using SKBitmap sequential = await RenderAsync(tile, style, bands: 1, parallelLabels: false);
        using SKBitmap parallel = await RenderAsync(tile, style, bands, parallelLabels: false);

        AssertSamePixels(sequential, parallel, allowedDifference: 0);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public async Task 帯の並列描画は線を含んでも逐次とほぼ同じ画素になる(int bands)
    {
        byte[] tile = MvtTestTile.Combine(
            MvtTestTile.CreateWater(rightEdge: 3072),
            MvtTestTile.CreateTransportation(("primary", Winding), ("path", Winding), ("rail", Straight), ("residential", Straight)));
        using var style = new OpenMapTilesStyle();

        using SKBitmap sequential = await RenderAsync(tile, style, bands: 1, parallelLabels: false);
        using SKBitmap parallel = await RenderAsync(tile, style, bands, parallelLabels: false);

        // 帯ごとに別のサーフェスへ描くと、Skia のクリップごとの丸めで、線の縁の半透明画素が最大 20 程度（/255）ずれ得る（画素の数 %。見た目は同じ）
        AssertSamePixels(sequential, parallel, allowedDifference: 24, allowedPixels: 0.05);
    }

    [Fact]
    public async Task 帯の数が画像の高さより多くても描ける()
    {
        byte[] tile = MvtTestTile.CreateWater();
        using var style = new OpenMapTilesStyle();

        using SKBitmap sequential = await RenderAsync(tile, style, bands: 1, parallelLabels: false, height: 3);
        using SKBitmap parallel = await RenderAsync(tile, style, bands: 8, parallelLabels: false, height: 3);

        AssertSamePixels(sequential, parallel, allowedDifference: 0);
    }

    [Fact]
    public async Task 注記の並列描画は注記が面と線の上に重なり衝突判定を共有する()
    {
        // シンボルが先に配置した矩形に、注記は衝突して描かれない。面（水域）の上に注記が重なる
        var style = new LabelProbeStyle();
        byte[] tile = MvtTestTile.CreateWater();

        using SKBitmap result = await RenderAsync(tile, style, bands: 2, parallelLabels: true);

        Assert.Equal(new SKColor(0, 0, 255), result.GetPixel(30, 30)); // シンボル（青）が配置された
        Assert.NotEqual(new SKColor(255, 0, 0), result.GetPixel(30, 30)); // 衝突した注記（赤）は描かれない
        Assert.Equal(new SKColor(0, 160, 0), result.GetPixel(100, 30)); // 衝突しない注記（緑）は描かれる
        Assert.Equal(new SKColor(0xA0, 0xC8, 0xF0), result.GetPixel(10, 100)); // 注記のない場所は面のまま
        Assert.Equal(["symbols", "roadNames", "labels"], style.LabelPasses);
    }

    [Fact]
    public async Task 注記を並列にしなくても帯の描画のあとに注記が描かれる()
    {
        var style = new LabelProbeStyle();

        using SKBitmap result = await RenderAsync(MvtTestTile.CreateWater(), style, bands: 3, parallelLabels: false);

        Assert.Equal(new SKColor(0, 0, 255), result.GetPixel(30, 30));
        Assert.Equal(new SKColor(0, 160, 0), result.GetPixel(100, 30));
        Assert.Equal(new SKColor(0xA0, 0xC8, 0xF0), result.GetPixel(10, 100));
    }

    [Fact]
    public async Task 帯の並列描画の途中でキャンセルされれば元のキャンセルの例外で終わる()
    {
        using var cts = new CancellationTokenSource();
        var style = new RecordingStyle { OnCall = pass => { if (pass == "polygons") { cts.Cancel(); } } };
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), style);

        // AggregateException ではなく OperationCanceledException になること
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => renderer.RenderViewAsync(Request(bands: 4, parallelLabels: true), cts.Token));
    }

    [Fact]
    public async Task 並列描画中のスタイルの失敗は包まれずに元の例外で伝わる()
    {
        var style = new RecordingStyle { OnCall = pass => { if (pass == "lines") { throw new InvalidOperationException("スタイルの失敗"); } } };
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), style);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => renderer.RenderViewAsync(Request(bands: 4, parallelLabels: false)));
        Assert.Equal("スタイルの失敗", exception.Message);
    }

    [Fact]
    public async Task 並列描画を繰り返しても共有のペイントが混ざらず同じ結果になる()
    {
        byte[] tile = MvtTestTile.Combine(
            MvtTestTile.CreateWater(rightEdge: 3072),
            MvtTestTile.CreateTransportation(("primary", Winding), ("path", Winding), ("rail", Straight), ("residential", Straight)));
        using var style = new OpenMapTilesStyle();

        using SKBitmap first = await RenderAsync(tile, style, bands: 8, parallelLabels: true);
        for (int i = 0; i < 20; i++)
        {
            using SKBitmap again = await RenderAsync(tile, style, bands: 8, parallelLabels: true);
            AssertSamePixels(first, again, allowedDifference: 0);
        }
    }

    [Fact]
    public async Task 帯の数が1未満の要求は引数の例外にする()
    {
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), new RecordingStyle());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => renderer.RenderViewAsync(Request(bands: 0, parallelLabels: false)));
    }

    private static async Task<SKBitmap> RenderAsync(byte[] tile, IVectorMapStyle style, int bands, bool parallelLabels, int height = 177)
    {
        var renderer = new VectorMapRenderer(SingleTileSource(tile), style);
        using VectorViewResult result = await renderer.RenderViewAsync(Request(bands, parallelLabels, height)).ConfigureAwait(false);
        return SKBitmap.FromImage(result.Image);
    }

    private static VectorViewRequest Request(int bands, bool parallelLabels, int height = 177)
    {
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);
        return new VectorViewRequest(latitude, longitude, 14.6, 301, height) { BandCount = bands, ParallelLabels = parallelLabels };
    }

    private static void AssertSamePixels(SKBitmap expected, SKBitmap actual, int allowedDifference, double allowedPixels = 0)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        int different = 0;
        int maxDifference = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                SKColor a = expected.GetPixel(x, y);
                SKColor b = actual.GetPixel(x, y);
                int difference = Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha))));
                if (difference > 0)
                {
                    different++;
                    maxDifference = Math.Max(maxDifference, difference);
                }
            }
        }

        Assert.True(maxDifference <= allowedDifference, $"{different} 画素が異なる（最大差 {maxDifference}、許容 {allowedDifference}）");
        Assert.True(different <= expected.Width * expected.Height * allowedPixels, $"{different} 画素が異なる（許容 {allowedPixels:P1}）");
    }

    private static MockVectorTileSource SingleTileSource(byte[] tile)
        => new(BaseZoom, (_, x, y) => x == TileX && y == TileY ? tile : null);

    private static (double Latitude, double Longitude) TileToLatLon(double tileX, double tileY, int zoom)
    {
        double n = Math.Pow(2, zoom);
        double longitude = (tileX / n * 360.0) - 180.0;
        double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * tileY / n)))) * 180.0 / Math.PI;
        return (latitude, longitude);
    }

    /// <summary>
    /// 注記の配置を調べるスタイル。シンボル（青）→ 道路名 → 注記の順に、同じ矩形・別の矩形へ配置を試み、
    /// 衝突判定が共有されていれば、同じ矩形の後続は描かれない。面は水域の色で全体を塗る。
    /// </summary>
    private sealed class LabelProbeStyle : IVectorMapStyle
    {
        private static readonly SKRect Shared = new(20, 20, 40, 40);
        private static readonly SKRect Free = new(90, 20, 110, 40);

        public List<string> LabelPasses { get; } = [];

        public string StyleId => "label-probe";

        public SKColor BackgroundColor => new(0xA0, 0xC8, 0xF0);

        public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
        {
        }

        public void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
        {
        }

        public void RenderSymbols(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        {
            this.LabelPasses.Add("symbols");
            Place(canvas, collisionGrid, viewport, Shared, new SKColor(0, 0, 255));
        }

        public void RenderLineLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        {
            this.LabelPasses.Add("roadNames");
            Place(canvas, collisionGrid, viewport, Shared, new SKColor(255, 0, 0));
        }

        public void RenderLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        {
            this.LabelPasses.Add("labels");
            Place(canvas, collisionGrid, viewport, Shared, new SKColor(255, 0, 0));
            Place(canvas, collisionGrid, viewport, Free, new SKColor(0, 160, 0));
        }

        private static void Place(SKCanvas canvas, LabelCollisionGrid grid, SKRect viewport, SKRect bounds, SKColor color)
        {
            if (grid.TryPlace(bounds, viewport))
            {
                using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(bounds, paint);
            }
        }
    }
}
