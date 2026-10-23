using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public sealed class OpenMapTilesQualityTests
{
    [Theory]
    [InlineData("motorway", "#9ad8a8")]
    [InlineData("trunk", "#ffb27a")]
    [InlineData("primary", "#ffe27a")]
    [InlineData("secondary", "#ffe27a")]
    [InlineData("tertiary", "#fff3c4")]
    public void 主要道路は階層ごとの色で一般道と区別できる(string kind, string color)
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        style.RenderLines(canvas, Tile("transportation", Line(kind)), 1f, 16);
        Assert.Equal(SKColor.Parse(color), bitmap.GetPixel(150, 150));
    }

    [Fact]
    public void 拡大すると道路幅が増え名称を載せる余地ができる()
    {
        using var style = new OpenMapTilesStyle();
        using var smaller = new SKBitmap(300, 300);
        using var larger = new SKBitmap(300, 300);
        using var a = new SKCanvas(smaller);
        using var b = new SKCanvas(larger);
        a.Clear(SKColors.Transparent);
        b.Clear(SKColors.Transparent);
        VectorTile tile = Tile("transportation", Line("minor"));
        style.RenderLines(a, tile, 1f, 16);
        style.RenderLines(b, tile, 1f, 18);
        int smallWidth = Enumerable.Range(0, 300).Count(y => smaller.GetPixel(150, y).Alpha > 128);
        int largeWidth = Enumerable.Range(0, 300).Count(y => larger.GetPixel(150, y).Alpha > 128);
        Assert.True(largeWidth > smallWidth * 2);
    }

    [Theory]
    [InlineData("landcover", "wood", "#c8dfb8")]
    [InlineData("landcover", "grass", "#d8e8c8")]
    [InlineData("landuse", "school", "#ebe3cf")]
    [InlineData("landuse", "residential", "#e6e1d8")]
    public void 土地利用を単一色にせず種類別に塗る(string layer, string kind, string color)
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        var area = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.Polygon,
            Properties = new Dictionary<string, object> { ["class"] = kind },
            GeometryParts = [[new(0, 0), new(300, 0), new(300, 300), new(0, 300), new(0, 0)]],
        };
        style.RenderPolygons(canvas, Tile(layer, area), 1f, 16);
        Assert.Equal(SKColor.Parse(color), bitmap.GetPixel(150, 150));
    }

    [Fact]
    public void 水路は道路の下に描き小道は破線で区別する()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var river = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            GeometryParts = [[new(150, 0), new(150, 300)]],
        };
        var tile = new VectorTile { Layers = [Layer("transportation", Line("secondary")), Layer("waterway", river)] };
        style.RenderLines(canvas, tile, 1f, 16);
        Assert.Equal(SKColor.Parse("#ffe27a"), bitmap.GetPixel(150, 150));
        Assert.Equal(SKColor.Parse("#a0c8f0"), bitmap.GetPixel(150, 100));

        canvas.Clear(SKColors.Transparent);
        style.RenderLines(canvas, Tile("transportation", Line("path")), 1f, 18);
        int drawn = Enumerable.Range(20, 260).Count(x => bitmap.GetPixel(x, 150).Alpha > 0);
        Assert.InRange(drawn, 50, 240);
    }

    private static VectorTileFeature Line(string kind) => new()
    {
        GeometryType = VectorGeometryType.LineString,
        Properties = new Dictionary<string, object> { ["class"] = kind },
        GeometryParts = [[new(0, 150), new(300, 150)]],
    };

    private static VectorTile Tile(string name, VectorTileFeature feature) => new() { Layers = [Layer(name, feature)] };

    private static VectorTileLayer Layer(string name, VectorTileFeature feature) => new() { Name = name, Extent = 300, Features = [feature] };
}
