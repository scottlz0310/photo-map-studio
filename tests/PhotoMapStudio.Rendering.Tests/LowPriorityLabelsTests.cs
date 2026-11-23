using PhotoMapStudio.Rendering;
using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

/// <summary>
/// 優先度の最も低い注記（番地）は、全タイルの通常の注記を置いた後に置く。先に走査したタイルの番地が、
/// 隣のタイルの駅・施設名を境界付近で押しのけないことを確かめる。
/// </summary>
public sealed class LowPriorityLabelsTests
{
    private const int BaseZoom = 14;
    private const int TileX = 14370;
    private const int TileY = 6488;
    private const int TileExtent = 200;

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public async Task 低い優先度の注記は全タイルの通常の注記の後に置く(int bands, bool parallelLabels)
    {
        var style = new OrderRecordingStyle();
        byte[] tile = MvtTestTile.CreateWater();
        var source = new MockVectorTileSource(BaseZoom, (_, x, y) => y == TileY && x is TileX or (TileX + 1) ? tile : null);
        var renderer = new VectorMapRenderer(source, style);

        // 隣り合う 2 枚のタイルの境界を中心にして、両方のタイルが表示範囲に入るようにする
        (double latitude, double longitude) = TileToLatLon(TileX + 1, TileY + 0.5, BaseZoom);
        using VectorViewResult result = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, 14.6, 301, 177) { BandCount = bands, ParallelLabels = parallelLabels });

        string[] passes = style.Passes;
        int labels = passes.Count(pass => pass == "labels");
        int low = passes.Count(pass => pass == "low");
        Assert.True(labels >= 2, $"通常の注記が 2 枚以上のタイルへ呼ばれる必要があります（{labels}）。");
        Assert.Equal(labels, low);
        Assert.True(
            Array.LastIndexOf(passes, "labels") < Array.IndexOf(passes, "low"),
            "低い優先度の注記は、全タイルの通常の注記の後に呼ばれる必要があります: " + string.Join(",", passes));
    }

    [Fact]
    public void 番地は隣のタイルの駅の記号を押しのけない()
    {
        // 左のタイルの番地と右のタイルの駅を、タイルの境界（x=200）から各 2 ピクセルに置く
        VectorTile left = Tile("housenumber", Point(198, 100, new() { ["housenumber"] = "12" }));
        VectorTile right = Tile("poi", Point(2, 100, new() { ["class"] = "railway", ["name"] = "駅", ["rank"] = 1L }));
        (VectorTile Tile, SKMatrix Matrix)[] tiles = [(left, SKMatrix.Identity), (right, SKMatrix.CreateTranslation(TileExtent, 0))];

        using SKBitmap rendererOrder = Render(tiles, lowPriorityLast: true);
        using SKBitmap perTileOrder = Render(tiles, lowPriorityLast: false);

        SKColor station = OpenMapTilesPoiSymbols.GetColor(PoiCategory.Transport);
        Assert.True(HasColor(rendererOrder, station), "全タイルの通常の注記の後に番地を置けば、駅の記号が描かれる必要があります。");

        // 番地を先に置くと駅を押しのけること（このテストが、順序の誤りを検出できること）
        Assert.False(HasColor(perTileOrder, station), "番地を先に置くと、駅が押しのけられる（回帰の再現条件）。");
    }

    private static SKBitmap Render((VectorTile Tile, SKMatrix Matrix)[] tiles, bool lowPriorityLast)
    {
        using var style = new OpenMapTilesStyle();
        var bitmap = new SKBitmap(TileExtent * 2, TileExtent);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var grid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, TileExtent * 2, TileExtent);

        if (lowPriorityLast)
        {
            foreach ((VectorTile tile, SKMatrix matrix) in tiles)
            {
                style.RenderLabels(canvas, tile, matrix, 17, grid, viewport, CancellationToken.None);
            }

            foreach ((VectorTile tile, SKMatrix matrix) in tiles)
            {
                style.RenderLowPriorityLabels(canvas, tile, matrix, 17, grid, viewport, CancellationToken.None);
            }
        }
        else
        {
            // 旧来の順序（タイルごとに、通常の注記の直後に番地を置く）
            foreach ((VectorTile tile, SKMatrix matrix) in tiles)
            {
                style.RenderLabels(canvas, tile, matrix, 17, grid, viewport, CancellationToken.None);
                style.RenderLowPriorityLabels(canvas, tile, matrix, 17, grid, viewport, CancellationToken.None);
            }
        }

        return bitmap;
    }

    private static bool HasColor(SKBitmap bitmap, SKColor color)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) == color)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static VectorTile Tile(string layer, VectorTileFeature feature)
        => new() { Layers = [new VectorTileLayer { Name = layer, Extent = TileExtent, Features = [feature] }] };

    private static VectorTileFeature Point(int x, int y, Dictionary<string, object> properties) => new()
    {
        GeometryType = VectorGeometryType.Point,
        Properties = properties,
        GeometryParts = [[new VectorPoint(x, y)]],
    };

    private static (double Latitude, double Longitude) TileToLatLon(double tileX, double tileY, int zoom)
    {
        double n = Math.Pow(2, zoom);
        double longitude = (tileX / n * 360.0) - 180.0;
        double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * tileY / n)))) * 180.0 / Math.PI;
        return (latitude, longitude);
    }

    /// <summary>注記の段階の呼び出し順を記録するスタイル。</summary>
    private sealed class OrderRecordingStyle : IVectorMapStyle
    {
        private readonly List<string> passes = [];

        public string[] Passes
        {
            get
            {
                lock (this.passes)
                {
                    return [.. this.passes];
                }
            }
        }

        public string StyleId => "order-recording";

        public SKColor BackgroundColor => SKColors.White;

        public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
        {
        }

        public void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
        {
        }

        public void RenderLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
            => this.Record("labels");

        public void RenderLowPriorityLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
            => this.Record("low");

        private void Record(string pass)
        {
            lock (this.passes)
            {
                this.passes.Add(pass);
            }
        }
    }
}
