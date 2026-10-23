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
        Assert.Equal(new SKColor(0xEB, 0xE8, 0xE1), style.BackgroundColor);
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

        // 中央のピクセルが水色（#A0C8F0）になっていること
        SKColor centerPixel = bitmap.GetPixel(50, 50);
        Assert.Equal(new SKColor(0xA0, 0xC8, 0xF0), centerPixel);
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

        // 道路中心（y = 50）が fill 色（#9AD8A8）
        SKColor roadCenter = bitmap.GetPixel(50, 50);
        Assert.Equal(new SKColor(0x9A, 0xD8, 0xA8), roadCenter);

        // 道路外側（y = 0）は透明のまま
        Assert.Equal(0, bitmap.GetPixel(50, 0).Alpha);
    }

    [Fact]
    public void transportationレイヤーのclassがrailの地物を鉄道ペイントで描画できる()
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
                                ["class"] = "rail",
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

        // 線中央（y = 50）が鉄道ペイント色（#777777）で描画されていること（アンチエイリアス考慮）
        SKColor railCenter = bitmap.GetPixel(50, 50);
        Assert.True(railCenter.Alpha > 0, "鉄道地物が描画されている必要があります。");
        Assert.Equal(railCenter.Red, railCenter.Green);
        Assert.Equal(railCenter.Red, railCenter.Blue);
    }

    [Fact]
    public void 注記がタイル変換行列を適用した画面座標へ正確に描画される()
    {
        using var style = new OpenMapTilesStyle();
        // 画面サイズ 256x256
        using var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // extent=4096 の中央 (2048, 2048) に地名点を配置
        var tile = new VectorTile
        {
            Layers =
            [
                new VectorTileLayer
                {
                    Name = "place",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.Point,
                            Properties = new Dictionary<string, object>
                            {
                                ["name:ja"] = "東京",
                            },
                            GeometryParts =
                            [
                                [new VectorPoint(2048, 2048)],
                            ],
                        },
                    ],
                },
            ],
        };

        // scale = 256 / 4096 = 1/16 の変換行列
        var matrix = SKMatrix.CreateScale(256f / 4096f, 256f / 4096f);
        var grid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, 256, 256);

        style.RenderLabels(canvas, tile, matrix, 14, grid, viewport);

        // 画面中央 (128, 128) 付近にテキスト（非透明）が描画されていること
        // 誤って二重変換されて (7, 7) 付近に飛んでいないことを検証
        bool foundTextAtCenter = false;
        for (int y = 120; y <= 135; y++)
        {
            for (int x = 110; x <= 145; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    foundTextAtCenter = true;
                    break;
                }
            }
            if (foundTextAtCenter)
            {
                break;
            }
        }

        Assert.True(foundTextAtCenter, "画面中央 (128, 128) 付近にテキストが描画されている必要があります。");

        // 誤った二重変換位置 (7, 7) 付近には描画されていないこと
        Assert.Equal(0, bitmap.GetPixel(7, 7).Alpha);
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

        Assert.Throws<ArgumentNullException>(() => style.RenderLabels(null!, tile, SKMatrix.Identity, 14, grid, viewport));
        Assert.Throws<ArgumentNullException>(() => style.RenderLabels(canvas, null!, SKMatrix.Identity, 14, grid, viewport));
        Assert.Throws<ArgumentNullException>(() => style.RenderLabels(canvas, tile, SKMatrix.Identity, 14, null!, viewport));
    }
}
