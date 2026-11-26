using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

/// <summary>航空写真などへ重ねる道路だけのスタイル。道路・道路名・経路番号だけを描き、ほかは透明のままにする。</summary>
public sealed class OpenMapTilesRoadOverlayStyleTests
{
    private const int Size = 200;
    private const int LineY = 100;

    // 端の丸い線端の影響を避けて、線の途中だけを調べる
    private const int From = 20;
    private const int To = 180;

    [Theory]
    [InlineData("motorway", 16, true)]
    [InlineData("trunk", 16, true)]
    [InlineData("primary", 16, true)]
    [InlineData("secondary", 16, true)]
    [InlineData("tertiary", 16, true)]
    [InlineData("minor", 16, true)]
    [InlineData("living_street", 16, true)]
    [InlineData("service", 16, true)]
    [InlineData("primary", 8, true)]
    [InlineData("track", 12, false)]
    [InlineData("track", 13, true)]
    [InlineData("path", 14, false)]
    [InlineData("path", 15, true)]
    [InlineData("pedestrian", 15, true)]
    [InlineData("cycleway", 15, true)]
    [InlineData("footway", 18, false)]
    [InlineData("steps", 18, false)]
    [InlineData("rail", 16, false)]
    [InlineData("transit", 16, false)]
    [InlineData("ferry", 16, false)]
    [InlineData("aerialway", 16, false)]
    [InlineData("raceway", 16, false)]
    public void 道路だけをズームの条件に従って描く(string roadClass, int zoom, bool drawn)
    {
        using SKBitmap bitmap = RenderLines(zoom, Line("transportation", roadClass));

        Assert.Equal(drawn, HasPaint(bitmap));
    }

    [Theory]
    [InlineData("footway", false)]
    [InlineData("steps", false)]
    [InlineData("corridor", false)]
    [InlineData("platform", false)]
    [InlineData("path", true)]
    [InlineData("pedestrian", true)]
    [InlineData("bridleway", true)]
    public void 歩道や階段や通路やホームは線も名前も描かない(string subclass, bool drawn)
    {
        using SKBitmap lines = RenderLines(18, Line("transportation", "path", ("subclass", subclass)));
        using SKBitmap names = RenderAllLabels(18, Line("transportation_name", "path", ("subclass", subclass), ("name", "通り")));

        Assert.Equal(drawn, HasPaint(lines));
        Assert.Equal(drawn, HasPaint(names));
    }

    [Theory]
    [InlineData("track")]
    [InlineData("path")]
    public void トンネルの林道と小道は描かない(string roadClass)
    {
        using SKBitmap bitmap = RenderLines(16, Line("transportation", roadClass, ("brunnel", "tunnel")));

        Assert.False(HasPaint(bitmap));
    }

    [Theory]
    [InlineData("track", null, true)]
    [InlineData("path", null, true)]
    [InlineData("primary", "tunnel", true)]
    [InlineData("minor", "tunnel", true)]
    [InlineData("service", null, false)]
    [InlineData("primary", null, false)]
    [InlineData("primary", "bridge", false)]
    public void 林道と小道とトンネルは破線でほかの舗装路は実線で描く(string roadClass, string? brunnel, bool dashed)
    {
        using SKBitmap bitmap = RenderLines(16, brunnel is null
            ? Line("transportation", roadClass)
            : Line("transportation", roadClass, ("brunnel", brunnel)));

        int covered = Enumerable.Range(From, To - From).Count(x => bitmap.GetPixel(x, LineY).Alpha > 0);

        if (dashed)
        {
            Assert.InRange(covered, 1, To - From - 20);
        }
        else
        {
            Assert.Equal(To - From, covered);
        }
    }

    [Theory]
    [InlineData("primary", "#ffe27a")]
    [InlineData("trunk", "#ffb27a")]
    [InlineData("minor", "#ffffff")]
    public void 芯線は標準の地図と同じ色で暗い縁を付ける(string roadClass, string color)
    {
        using SKBitmap bitmap = RenderLines(18, Line("transportation", roadClass));

        Assert.Equal(SKColor.Parse(color), bitmap.GetPixel(Size / 2, LineY));
        SKColor[] column = Enumerable.Range(LineY - 8, 17).Select(y => bitmap.GetPixel(Size / 2, y)).ToArray();
        // 縁は細いので、芯線や透明な画素と混ざる。暗い色が勝つ画素があることを調べる
        Assert.Contains(column, pixel => pixel.Alpha > 150 && pixel.Red < 150 && pixel.Green < 150);
    }

    [Fact]
    public void 交差点では下の道路の縁が上の道路の芯線を分断しない()
    {
        var major = Feature("primary", [new(0, LineY), new(Size, LineY)]);
        var minor = Feature("minor", [new(Size / 2, 0), new(Size / 2, Size)]);

        using SKBitmap bitmap = RenderLines(18, Tile("transportation", minor, major));

        Assert.All(
            Enumerable.Range((Size / 2) - 6, 13),
            x => Assert.Equal(SKColor.Parse("#ffe27a"), bitmap.GetPixel(x, LineY)));
    }

    [Fact]
    public void 面は描かず写真が見えるように透明のままにする()
    {
        using var style = new OpenMapTilesRoadOverlayStyle();
        using SKBitmap bitmap = NewBitmap();
        using var canvas = new SKCanvas(bitmap);

        style.RenderPolygons(canvas, Decode(MvtTestTile.CreateWater()), Size / 4096f, 16, CancellationToken.None);

        Assert.Equal(SKColors.Transparent, style.BackgroundColor);
        Assert.False(HasPaint(bitmap));
    }

    [Theory]
    [InlineData("place")]
    [InlineData("poi")]
    [InlineData("water_name")]
    public void 地名や施設名は描かない(string layer)
    {
        var point = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.Point,
            Properties = new Dictionary<string, object> { ["name"] = "旭川", ["class"] = "city", ["rank"] = 1L },
            GeometryParts = [[new(Size / 2, LineY)]],
        };

        using SKBitmap bitmap = RenderAllLabels(17, Tile(layer, point));

        Assert.False(HasPaint(bitmap));
    }

    [Theory]
    [InlineData("primary", 16, true)]
    [InlineData("minor", 16, true)]
    [InlineData("minor", 14, false)]
    [InlineData("track", 14, true)]
    [InlineData("path", 17, true)]
    [InlineData("aerialway", 16, false)]
    [InlineData("rail", 16, false)]
    public void 道路名を描き索道や鉄道の名前は描かない(string roadClass, int zoom, bool drawn)
    {
        using SKBitmap bitmap = RenderAllLabels(zoom, Line("transportation_name", roadClass, ("name", "四条通")));

        Assert.Equal(drawn, HasPaint(bitmap));
    }

    [Fact]
    public void 道路名には白いフチを付ける()
    {
        using SKBitmap bitmap = RenderAllLabels(16, Line("transportation_name", "primary", ("name", "四条通")));

        SKColor[] pixels = bitmap.Pixels;
        Assert.Contains(pixels, pixel => pixel == SKColors.White);
        Assert.Contains(pixels, pixel => pixel.Alpha > 200 && pixel.Red < 80);
    }

    [Theory]
    [InlineData("JP:national", "12", true)]
    [InlineData("JP:prefectural", "90", true)]
    [InlineData("JP:municipal", "3", false)]
    public void 経路番号の盾を描く(string network, string reference, bool drawn)
    {
        using var style = new OpenMapTilesRoadOverlayStyle();
        using SKBitmap bitmap = NewBitmap();
        using var canvas = new SKCanvas(bitmap);
        VectorTile tile = Line("transportation_name", "primary", ("route_1_network", network), ("route_1_ref", reference));

        style.RenderSymbols(canvas, tile, SKMatrix.CreateIdentity(), 16, new LabelCollisionGrid(), new SKRect(0, 0, Size, Size), CancellationToken.None);

        Assert.Equal(drawn, HasPaint(bitmap));
    }

    [Theory]
    [InlineData("polygons")]
    [InlineData("lines")]
    [InlineData("symbols")]
    [InlineData("lineLabels")]
    [InlineData("labels")]
    [InlineData("lowPriorityLabels")]
    public void キャンセル済みなら描画のパスは例外で終わる(string pass)
    {
        VectorTile tile = Line("transportation", "primary");
        using var style = new OpenMapTilesRoadOverlayStyle();
        using SKBitmap bitmap = NewBitmap();
        using var canvas = new SKCanvas(bitmap);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var grid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, Size, Size);
        SKMatrix matrix = SKMatrix.CreateIdentity();
        CancellationToken token = cancellation.Token;

        Action render = pass switch
        {
            "polygons" => () => style.RenderPolygons(canvas, tile, 1f, 16, token),
            "lines" => () => style.RenderLines(canvas, tile, 1f, 16, token),
            "symbols" => () => style.RenderSymbols(canvas, tile, matrix, 16, grid, viewport, token),
            "lineLabels" => () => style.RenderLineLabels(canvas, tile, matrix, 16, grid, viewport, token),
            "labels" => () => style.RenderLabels(canvas, tile, matrix, 16, grid, viewport, token),
            "lowPriorityLabels" => () => style.RenderLowPriorityLabels(canvas, tile, matrix, 16, grid, viewport, token),
            _ => throw new ArgumentOutOfRangeException(nameof(pass)),
        };

        Assert.ThrowsAny<OperationCanceledException>(render);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, true)]
    public async Task 表示範囲の画像は道路以外が透明になる(int bandCount, bool parallelLabels)
    {
        const int Zoom = 14;
        const int TileX = 100;
        const int TileY = 100;
        byte[] data = MvtTestTile.Combine(
            MvtTestTile.CreateWater(),
            MvtTestTile.CreateTransportation(("primary", [(0, 2048), (4096, 2048)])));
        using var style = new OpenMapTilesRoadOverlayStyle();
        var renderer = new VectorMapRenderer(
            new MockVectorTileSource(Zoom, (_, x, y) => x == TileX && y == TileY ? data : null), style);
        double n = Math.Pow(2, Zoom);
        double longitude = ((TileX + 0.5) / n * 360.0) - 180.0;
        double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * (TileY + 0.5) / n)))) * 180.0 / Math.PI;
        var request = new VectorViewRequest(latitude, longitude, Zoom, 64, 64) { BandCount = bandCount, ParallelLabels = parallelLabels };

        using VectorViewResult result = await renderer.RenderViewAsync(request);
        using SKBitmap bitmap = SKBitmap.FromImage(result.Image);

        Assert.Equal(0, bitmap.GetPixel(2, 2).Alpha);
        Assert.Equal(0, bitmap.GetPixel(32, 60).Alpha);
        Assert.Equal(255, bitmap.GetPixel(32, 32).Alpha);
    }

    private static SKBitmap RenderLines(int zoom, VectorTile tile)
    {
        using var style = new OpenMapTilesRoadOverlayStyle();
        SKBitmap bitmap = NewBitmap();
        using var canvas = new SKCanvas(bitmap);
        style.RenderLines(canvas, tile, 1f, zoom, CancellationToken.None);
        return bitmap;
    }

    private static SKBitmap RenderAllLabels(int zoom, VectorTile tile)
    {
        using var style = new OpenMapTilesRoadOverlayStyle();
        SKBitmap bitmap = NewBitmap();
        using var canvas = new SKCanvas(bitmap);
        var grid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, Size, Size);
        SKMatrix matrix = SKMatrix.CreateIdentity();
        style.RenderLineLabels(canvas, tile, matrix, zoom, grid, viewport, CancellationToken.None);
        style.RenderLabels(canvas, tile, matrix, zoom, grid, viewport, CancellationToken.None);
        style.RenderLowPriorityLabels(canvas, tile, matrix, zoom, grid, viewport, CancellationToken.None);
        return bitmap;
    }

    private static SKBitmap NewBitmap()
    {
        var bitmap = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    private static bool HasPaint(SKBitmap bitmap) => bitmap.Pixels.Any(pixel => pixel.Alpha > 0);

    private static VectorTile Line(string layer, string roadClass, params (string Key, string Value)[] properties)
    {
        VectorTileFeature feature = Feature(roadClass, [new(0, LineY), new(Size, LineY)], properties);
        return Tile(layer, feature);
    }

    private static VectorTileFeature Feature(string roadClass, VectorPoint[] points, params (string Key, string Value)[] properties)
    {
        var values = new Dictionary<string, object> { ["class"] = roadClass };
        foreach ((string key, string value) in properties)
        {
            values[key] = value;
        }

        return new VectorTileFeature { GeometryType = VectorGeometryType.LineString, Properties = values, GeometryParts = [points] };
    }

    private static VectorTile Tile(string layer, params VectorTileFeature[] features)
        => new() { Layers = [new VectorTileLayer { Name = layer, Extent = Size, Features = [.. features] }] };

    private static VectorTile Decode(byte[] data)
    {
        Assert.True(VectorTileDecoder.TryDecode(data, out VectorTile tile));
        return tile;
    }
}
