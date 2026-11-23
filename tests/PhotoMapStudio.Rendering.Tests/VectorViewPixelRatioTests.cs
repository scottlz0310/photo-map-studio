using System.Collections.Concurrent;

using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

/// <summary>
/// <see cref="VectorViewRequest.PixelRatio"/>（表示の画素密度）で、スタイルの大きさとズームの判定が論理ピクセルになり、
/// キャンバスが画素密度倍に拡大されて描かれること。既定値（1.0）では従来と同じ画像になること。
/// </summary>
public class VectorViewPixelRatioTests
{
    private const int BaseZoom = 14;
    private const int TileX = 14370;
    private const int TileY = 6488;

    // 論理ピクセルの基準の画像寸法と、論理ズーム。タイルのズーム 14 に対して表示倍率は 2^(15-14) = 2、タイルの 1 画素は 256×2/4096 = 0.125 論理ピクセル
    private const int LogicalWidth = 300;
    private const int LogicalHeight = 160;
    private const double LogicalZoom = 15.0;
    private const float ExpectedTileScale = 0.125f;

    public static TheoryData<double, int, bool> RatiosAndDrawModes()
    {
        var data = new TheoryData<double, int, bool> { { 1.0, 1, false } };
        foreach (double ratio in new[] { 1.25, 1.5, 1.75, 2.0 })
        {
            data.Add(ratio, 1, false); // 逐次
            data.Add(ratio, 3, false); // 帯の並列（注記は本体へ直接）
            data.Add(ratio, 3, true); // 帯の並列と注記の並列
            data.Add(ratio, 1, true); // 注記だけ並列
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RatiosAndDrawModes))]
    public async Task スタイルへ渡るズームと倍率と範囲は論理ピクセルでキャンバスは画素密度倍に拡大される(double ratio, int bands, bool parallelLabels)
    {
        var style = new ProbeStyle();
        byte[] tile = MvtTestTile.CreateWater();

        using SKBitmap result = await RenderAsync(tile, style, ratio, bands, parallelLabels);

        Assert.Equal((int)Math.Round(LogicalWidth * ratio), result.Width);
        Assert.Equal((int)Math.Round(LogicalHeight * ratio), result.Height);
        Assert.NotEmpty(style.Calls);
        foreach (ProbeCall call in style.Calls)
        {
            // スタイルの判定に使うズームは、画素密度を除いた論理ズームを切り捨てた値
            Assert.Equal(15, call.Zoom);
            if (call.Pass is "polygons" or "lines")
            {
                // 面・線: タイルの座標系の拡大は論理ピクセルで求め、その外側のキャンバスが画素密度倍
                Assert.Equal(ExpectedTileScale, call.TileScale, 1e-5);
                Assert.Equal(ExpectedTileScale * ratio, call.CanvasScale, 1e-4);
            }
            else
            {
                // 注記: キャンバスが画素密度倍で、タイルから画面への行列と衝突判定の範囲は論理ピクセル
                Assert.Equal(ratio, call.CanvasScale, 1e-4);
                Assert.Equal(ExpectedTileScale, call.TileScale, 1e-5);
                Assert.Equal(LogicalWidth, call.Viewport.Width, 1e-3);
                Assert.Equal(LogicalHeight, call.Viewport.Height, 1e-3);
            }
        }
    }

    [Theory]
    [InlineData(1.0, 1, false)]
    [InlineData(1.5, 1, false)]
    [InlineData(1.5, 3, true)]
    [InlineData(2.0, 3, false)]
    [InlineData(2.0, 1, true)]
    public async Task 注記が描く図形の物理ピクセル寸法は画素密度倍になる(double ratio, int bands, bool parallelLabels)
    {
        var style = new ProbeStyle();

        using SKBitmap result = await RenderAsync(MvtTestTile.CreateWater(), style, ratio, bands, parallelLabels);

        // 論理ピクセルの (20,20)-(40,40) の矩形が、物理ピクセルでは画素密度倍の位置・大きさになる
        int left = (int)Math.Round(20 * ratio);
        int right = (int)Math.Round(40 * ratio);
        int middleY = (int)Math.Round(30 * ratio);
        Assert.Equal(ProbeStyle.LabelColor, result.GetPixel(left + 1, middleY));
        Assert.Equal(ProbeStyle.LabelColor, result.GetPixel(right - 2, middleY));
        Assert.Equal(ProbeStyle.BackgroundColorValue, result.GetPixel(left - 2, middleY));
        Assert.Equal(ProbeStyle.BackgroundColorValue, result.GetPixel(right + 2, middleY));
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public async Task 地理的な位置は画素密度によらず同じで物理ピクセルでは画素密度倍になる(double ratio)
    {
        byte[] tile = MvtTestTile.CreateWater(rightEdge: 3072);
        using var style = new OpenMapTilesStyle();

        using SKBitmap baseline = await RenderAsync(tile, style, 1.0, bands: 1, parallelLabels: false);
        using SKBitmap scaled = await RenderAsync(tile, style, ratio, bands: 1, parallelLabels: false);

        // 水域の右端（タイルの 75%）の位置を、中央の行で探す
        int baselineEdge = FindWaterEdge(baseline);
        int scaledEdge = FindWaterEdge(scaled);
        Assert.InRange(baselineEdge, 1, LogicalWidth - 2);
        Assert.InRange(scaledEdge / ratio, baselineEdge - 1.5, baselineEdge + 1.5);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task 面と線の描画は画素密度倍の画像でも基準の画像を拡大したものとほぼ同じになる(double ratio)
    {
        (int X, int Y)[] winding = [(100, 3900), (900, 2100), (2100, 2500), (3200, 600), (3900, 1500)];
        byte[] tile = MvtTestTile.Combine(
            MvtTestTile.CreateWater(rightEdge: 3072),
            MvtTestTile.CreateTransportation(("primary", winding), ("residential", [(0, 1800), (4096, 2300)])));
        using var style = new OpenMapTilesStyle();

        using SKBitmap baseline = await RenderAsync(tile, style, 1.0, bands: 1, parallelLabels: false);
        using SKBitmap scaled = await RenderAsync(tile, style, ratio, bands: 1, parallelLabels: false);
        using SKBitmap reduced = scaled.Resize(new SKImageInfo(baseline.Width, baseline.Height, baseline.ColorType, baseline.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell))!;

        // 縮小で線の縁の濃さがわずかに違うため、平均の差で比べる。実測は 0.3 程度（/255）。
        // 画素密度を伝えずに物理ピクセルのズームだけ渡すと、線の太さがずれて 1.5〜2.2 になる
        double meanDifference = MeanAbsoluteDifference(baseline, reduced);
        Assert.True(meanDifference < 0.8, $"平均の差 {meanDifference:F2}（/255）");
    }

    [Fact]
    public async Task 画素密度を指定しない要求と標準の密度を指定した要求は同じ画素になる()
    {
        byte[] tile = MvtTestTile.Combine(
            MvtTestTile.CreateWater(rightEdge: 3072),
            MvtTestTile.CreateTransportation(("primary", [(100, 3900), (3200, 600)])));
        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(SingleTileSource(tile), style);
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);

        using VectorViewResult withoutRatio = await renderer.RenderViewAsync(new VectorViewRequest(latitude, longitude, 14.6, 301, 177));
        using VectorViewResult withRatio = await renderer.RenderViewAsync(new VectorViewRequest(latitude, longitude, 14.6, 301, 177) { PixelRatio = 1.0 });
        using SKBitmap a = SKBitmap.FromImage(withoutRatio.Image);
        using SKBitmap b = SKBitmap.FromImage(withRatio.Image);

        Assert.Equal(0, MeanAbsoluteDifference(a, b));
    }

    [Fact]
    public async Task 画像の寸法が画素密度で割り切れなくても描ける()
    {
        var style = new ProbeStyle();
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), style);
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);

        using VectorViewResult result = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, 14.6, 301, 177) { PixelRatio = 1.5, BandCount = 3, ParallelLabels = true });

        Assert.Equal(301, result.Image.Width);
        Assert.Equal(177, result.Image.Height);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task 不正な画素密度は引数の例外にする(double ratio)
    {
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), new RecordingStyle());
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => renderer.RenderViewAsync(new VectorViewRequest(latitude, longitude, 14.6, 301, 177) { PixelRatio = ratio }));
    }

    private static async Task<SKBitmap> RenderAsync(byte[] tile, IVectorMapStyle style, double ratio, int bands, bool parallelLabels)
    {
        var renderer = new VectorMapRenderer(SingleTileSource(tile), style);
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);

        // 物理ピクセルのズームと画像寸法（呼び出し側は、表示の物理ピクセルに合わせて渡す）。論理ズームは常に 15.0
        var request = new VectorViewRequest(
            latitude,
            longitude,
            LogicalZoom + Math.Log2(ratio),
            (int)Math.Round(LogicalWidth * ratio),
            (int)Math.Round(LogicalHeight * ratio))
        {
            PixelRatio = ratio,
            BandCount = bands,
            ParallelLabels = parallelLabels,
        };
        using VectorViewResult result = await renderer.RenderViewAsync(request).ConfigureAwait(false);
        return SKBitmap.FromImage(result.Image);
    }

    private static int FindWaterEdge(SKBitmap bitmap)
    {
        // OpenMapTilesStyle の水域は面全体を塗る（背景と別の色）。中央の行で、水域の色でなくなる最初の位置を探す
        int y = bitmap.Height / 2;
        SKColor water = bitmap.GetPixel(2, y);
        for (int x = 3; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y) != water)
            {
                return x;
            }
        }

        return bitmap.Width;
    }

    private static double MeanAbsoluteDifference(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        long total = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                SKColor a = expected.GetPixel(x, y);
                SKColor b = actual.GetPixel(x, y);
                total += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
            }
        }

        return total / (3.0 * expected.Width * expected.Height);
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

    private sealed record ProbeCall(string Pass, int Zoom, float CanvasScale, float TileScale, SKRect Viewport);

    /// <summary>スタイルへ渡された値（ズーム・タイルの倍率・キャンバスの拡大・衝突判定の範囲）を記録し、論理ピクセルの矩形を注記として描くスタイル。</summary>
    private sealed class ProbeStyle : IVectorMapStyle
    {
        public static readonly SKColor LabelColor = new(255, 0, 0);

        public static readonly SKColor BackgroundColorValue = SKColors.White;

        public ConcurrentBag<ProbeCall> Calls { get; } = [];

        public string StyleId => "pixel-ratio-probe";

        public SKColor BackgroundColor => BackgroundColorValue;

        public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
            => this.Calls.Add(new ProbeCall("polygons", zoom, canvas.TotalMatrix.ScaleX, tileScale, SKRect.Empty));

        public void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
            => this.Calls.Add(new ProbeCall("lines", zoom, canvas.TotalMatrix.ScaleX, tileScale, SKRect.Empty));

        public void RenderSymbols(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
            => this.Calls.Add(new ProbeCall("symbols", zoom, canvas.TotalMatrix.ScaleX, tileToCanvasMatrix.ScaleX, viewport));

        public void RenderLineLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
            => this.Calls.Add(new ProbeCall("roadNames", zoom, canvas.TotalMatrix.ScaleX, tileToCanvasMatrix.ScaleX, viewport));

        public void RenderLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        {
            this.Calls.Add(new ProbeCall("labels", zoom, canvas.TotalMatrix.ScaleX, tileToCanvasMatrix.ScaleX, viewport));
            using var paint = new SKPaint { Color = LabelColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(20, 20, 40, 40), paint);
        }
    }
}
