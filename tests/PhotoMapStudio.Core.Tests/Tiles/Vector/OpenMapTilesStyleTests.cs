using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class OpenMapTilesStyleTests
{
    [Fact]
    public void StyleIdとBackgroundColorが期待通り定義されている()
    {
        using var style = new OpenMapTilesStyle();

        Assert.Equal("openmaptiles", style.StyleId);
        Assert.Equal(new SKColor(0xF2, 0xEF, 0xE9), style.BackgroundColor);
    }

    [Fact]
    public void 水域ポリゴンを描画すると水色ピクセルがキャンバスに反映される()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(style.BackgroundColor);

        var tile = new VectorTile
        {
            Layers =
            [
                new VectorTileLayer
                {
                    Name = "water",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.Polygon,
                            GeometryParts =
                            [
                                [
                                    new VectorPoint(0, 0),
                                    new VectorPoint(4096, 0),
                                    new VectorPoint(4096, 4096),
                                    new VectorPoint(0, 4096),
                                    new VectorPoint(0, 0),
                                ],
                            ],
                        },
                    ],
                },
            ],
        };

        canvas.Scale(100f / 4096f, 100f / 4096f);
        style.RenderPolygons(canvas, tile, 100f / 4096f, 14);

        // 中央のピクセルが水色（#A5D6E7）になっていること
        SKColor centerPixel = bitmap.GetPixel(50, 50);
        Assert.Equal(new SKColor(0xA5, 0xD6, 0xE7), centerPixel);
    }

    [Fact]
    public void 道路ラインを描画するとcasingとfillが反映される()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var tile = new VectorTile
        {
            Layers =
            [
                new VectorTileLayer
                {
                    Name = "transportation",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.LineString,
                            Properties = new Dictionary<string, object>
                            {
                                ["class"] = "motorway",
                            },
                            GeometryParts =
                            [
                                [
                                    new VectorPoint(0, 2048),
                                    new VectorPoint(4096, 2048),
                                ],
                            ],
                        },
                    ],
                },
            ],
        };

        float tileScale = 100f / 4096f;
        canvas.Scale(tileScale, tileScale);
        style.RenderLines(canvas, tile, tileScale, 14);

        // 道路中心（y = 50）が fill 色（#F9B29C）
        SKColor roadCenter = bitmap.GetPixel(50, 50);
        Assert.Equal(new SKColor(0xF9, 0xB2, 0x9C), roadCenter);

        // 道路外側（y = 0）は透明のまま
        Assert.Equal(0, bitmap.GetPixel(50, 0).Alpha);
    }

    [Fact]
    public void null引数を渡すとArgumentNullExceptionをスローする()
    {
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(bitmap);
        var tile = new VectorTile();
        var grid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, 10, 10);

        Assert.Throws<ArgumentNullException>(() => style.RenderPolygons(null!, tile, 1f, 14));
        Assert.Throws<ArgumentNullException>(() => style.RenderPolygons(canvas, null!, 1f, 14));

        Assert.Throws<ArgumentNullException>(() => style.RenderLines(null!, tile, 1f, 14));
        Assert.Throws<ArgumentNullException>(() => style.RenderLines(canvas, null!, 1f, 14));

        Assert.Throws<ArgumentNullException>(() => style.RenderLabels(null!, tile, 1f, 14, grid, viewport));
        Assert.Throws<ArgumentNullException>(() => style.RenderLabels(canvas, null!, 1f, 14, grid, viewport));
        Assert.Throws<ArgumentNullException>(() => style.RenderLabels(canvas, tile, 1f, 14, null!, viewport));
    }
}
