using PhotoMapStudio.Rendering;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

public sealed class GsiVectorStyleTests
{
    [Theory]
    [InlineData("vt_text")]
    [InlineData("knj")]
    public void 線形状の注記は線に沿わせポイント注記は従来の表示を保つ(string nameKey)
    {
        using var style = new GsiVectorStyle();
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        var line = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            Properties = new Dictionary<string, object> { [nameKey] = "北２丁目通り" },
            GeometryParts = [[new(150, 20), new(150, 280)]],
        };
        var tile = new VectorTile { Layers = [new VectorTileLayer { Name = "Anno", Features = [line] }] };
        var grid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, 300, 300);
        style.RenderLineLabels(canvas, tile, SKMatrix.Identity, 18, grid, viewport, CancellationToken.None);
        SKColor[] linePixels = bitmap.Pixels;
        Assert.Contains(linePixels, color => color.Red < 160);
        // 線注記をポイント注記のパスで二重に描画しない。
        style.RenderLabels(canvas, tile, SKMatrix.Identity, 18, grid, viewport, CancellationToken.None);
        Assert.Equal(linePixels, bitmap.Pixels);
    }

    [Fact]
    public void StyleIdとBackgroundColorが期待通り定義されている()
    {
        using var style = new GsiVectorStyle();
        Assert.Equal("gsi", style.StyleId);
        Assert.Equal(new SKColor(0xF8, 0xF7, 0xF2), style.BackgroundColor);
    }

    [Fact]
    public void 水域および建物のポリゴンが正しく描画される()
    {
        using var style = new GsiVectorStyle();
        using var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(style.BackgroundColor);

        var tile = new VectorTile
        {
            Layers =
            [
                new VectorTileLayer
                {
                    Name = "WA",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.Polygon,
                            GeometryParts =
                            [
                                [new(0, 0), new(4096, 0), new(4096, 4096), new(0, 4096), new(0, 0)],
                            ],
                        },
                    ],
                },
                new VectorTileLayer
                {
                    Name = "BldA",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.Polygon,
                            GeometryParts =
                            [
                                [new(100, 100), new(500, 100), new(500, 500), new(100, 500), new(100, 100)],
                            ],
                        },
                    ],
                },
            ],
        };

        float tileScale = 256f / 4096f;
        canvas.Scale(tileScale, tileScale);
        style.RenderPolygons(canvas, tile, tileScale, 14, CancellationToken.None);

        SKColor waterPixel = bitmap.GetPixel(128, 128);
        Assert.Equal(new SKColor(0xC4, 0xE5, 0xF8), waterPixel);
    }

    [Fact]
    public void 道路や等高線や鉄道のラインが正しく描画される()
    {
        using var style = new GsiVectorStyle();
        using var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var tile = new VectorTile
        {
            Layers =
            [
                new VectorTileLayer
                {
                    Name = "Cntr",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.LineString,
                            GeometryParts = [[new(0, 1000), new(4096, 1000)]],
                        },
                    ],
                },
                new VectorTileLayer
                {
                    Name = "RdCL",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.LineString,
                            GeometryParts = [[new(0, 2048), new(4096, 2048)]],
                            Properties = new Dictionary<string, object>
                            {
                                ["rdCtg"] = 0L, // 国道
                                ["rnkWidth"] = 2L,
                            },
                        },
                    ],
                },
                new VectorTileLayer
                {
                    Name = "RailCL",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.LineString,
                            GeometryParts = [[new(0, 3000), new(4096, 3000)]],
                        },
                    ],
                },
            ],
        };

        float tileScale = 256f / 4096f;
        canvas.Scale(tileScale, tileScale);
        style.RenderLines(canvas, tile, tileScale, 14, CancellationToken.None);

        // y = 128 付近に国道が描画されていること
        SKColor roadPixel = bitmap.GetPixel(128, 128);
        Assert.True(roadPixel.Alpha > 0);
    }

    [Theory]
    [InlineData("高速自動車国道等", "19.5m以上", "Highway", 5.2f)]
    [InlineData("国道", "5.5m-13m未満", "National", 3.2f)]
    [InlineData("主要道路", "3m-5.5m未満", "National", 3.2f)]
    [InlineData("都道府県道", "3m-5.5m未満", "Prefectural", 2.8f)]
    [InlineData("都道府県道", "5.5m-13m未満", "Prefectural", 3.2f)]
    [InlineData("市区町村道等", "3m未満", "Minor", 1.5f)]
    [InlineData("市区町村道等", "3m-5.5m未満", "Minor", 2.2f)]
    [InlineData("市区町村道等", "5.5m-13m未満", "Minor", 3.2f)]
    [InlineData("市区町村道等", "13m-19.5m未満", "Minor", 4.2f)]
    [InlineData("市区町村道等", "19.5m以上", "Minor", 5.2f)]
    [InlineData("市区町村道等", "不明", "Minor", 2.2f)]
    [InlineData("その他", "不明", "Minor", 2.2f)]
    [InlineData("0", "2", "National", 3.2f)]
    public void 実スキーマ文字列属性による道路分類と幅員が正確に解決される(
        string rdCtg,
        string rnkWidth,
        string expectedKind,
        float expectedWidth)
    {
        var feature = new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            Properties = new Dictionary<string, object>
            {
                ["vt_rdctg"] = rdCtg,
                ["vt_rnkwidth"] = rnkWidth,
            },
        };

        var (kind, width) = GsiVectorStyle.GetRoadCategory(feature);

        Assert.Equal(expectedKind, kind.ToString());
        Assert.Equal(expectedWidth, width);
    }

    [Theory]
    [InlineData("高速自動車国道等", "19.5m以上", 76, 175, 80)] // #4CAF50
    [InlineData("国道", "5.5m-13m未満", 230, 81, 0)]          // #E65100
    [InlineData("都道府県道", "3m-5.5m未満", 245, 127, 23)]    // #F57F17
    [InlineData("市区町村道等", "5.5m-13m未満", 255, 255, 255)] // SKColors.White
    public void 実スキーマ道路属性に応じて道路固有の色で線が描画される(
        string rdCtg,
        string rnkWidth,
        byte expectedR,
        byte expectedG,
        byte expectedB)
    {
        using var style = new GsiVectorStyle();
        using var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var tile = new VectorTile
        {
            Layers =
            [
                new VectorTileLayer
                {
                    Name = "RdCL",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.LineString,
                            GeometryParts = [[new(0, 2048 + 8), new(4096, 2048 + 8)]],
                            Properties = new Dictionary<string, object>
                            {
                                ["vt_rdctg"] = rdCtg,
                                ["vt_rnkwidth"] = rnkWidth,
                            },
                        },
                    ],
                },
            ],
        };

        float tileScale = 256f / 4096f;
        canvas.Scale(tileScale, tileScale);
        style.RenderLines(canvas, tile, tileScale, 14, CancellationToken.None);

        // y = 128 付近の道路中心ピクセル色をサンプリング
        SKColor roadPixel = bitmap.GetPixel(128, 128);
        Assert.Equal(expectedR, roadPixel.Red);
        Assert.Equal(expectedG, roadPixel.Green);
        Assert.Equal(expectedB, roadPixel.Blue);
        Assert.Equal(255, roadPixel.Alpha);
    }

    [Theory]
    [InlineData("国道", 45L, "市区町村道等", 43L, 230, 81, 0)]
    [InlineData("都道府県道", 44L, "市区町村道等", 43L, 245, 127, 23)]
    [InlineData("高速自動車国道等", 46L, "国道", 45L, 76, 175, 80)]
    [InlineData("国道", null, "市区町村道等", null, 230, 81, 0)]
    [InlineData("都道府県道", null, "市区町村道等", null, 245, 127, 23)]
    [InlineData("高速自動車国道等", null, "国道", null, 76, 175, 80)]
    public void 同じ階層の交差部は描画順が上の道路の塗りが地物の順によらず途切れない(
        string upperCategory,
        long? upperDrawOrder,
        string lowerCategory,
        long? lowerDrawOrder,
        byte expectedR,
        byte expectedG,
        byte expectedB)
    {
        // 上になる道路を先に並べ、地物の順に描くと下の道路に上書きされる配置にする
        using SKBitmap bitmap = RenderRoadCrossing(
            Road(Horizontal, upperCategory, level: 0, upperDrawOrder),
            Road(Vertical, lowerCategory, level: 0, lowerDrawOrder));

        Assert.Equal(new SKColor(expectedR, expectedG, expectedB), bitmap.GetPixel(128, 128));
    }

    [Fact]
    public void 上の階層の道路は下の階層の道路をcasingごと覆う()
    {
        using SKBitmap bitmap = RenderRoadCrossing(
            Road(Vertical, "市区町村道等", level: 1, drawOrder: 53),
            Road(Horizontal, "国道", level: 0, drawOrder: 45));

        // 交差部の中央は上の道路の塗り、その左右の縁は上の道路の casing
        Assert.Equal(SKColors.White, bitmap.GetPixel(128, 128));
        Assert.Equal(new SKColor(0xB0, 0xB0, 0xB0), bitmap.GetPixel(116, 128));
    }

    [Fact]
    public void 交差部で区切られた道路の継ぎ目から下の道路が透けない()
    {
        // 上の道路は、画素の中心にある交差点で 2 つの地物に分かれる（実データは交差点ごとに分かれる）
        using SKBitmap bitmap = RenderRoadCrossing(
            Road([new(0, 2056), new(2056, 2056)], "国道", level: 0, drawOrder: 45),
            Road([new(2056, 2056), new(4096, 2056)], "国道", level: 0, drawOrder: 45),
            Road(Vertical, "市区町村道等", level: 0, drawOrder: 43));

        Assert.Equal(new SKColor(0xE6, 0x51, 0x00), bitmap.GetPixel(128, 128));
    }

    private static readonly VectorPoint[] Horizontal = [new(0, 2056), new(4096, 2056)];

    private static readonly VectorPoint[] Vertical = [new(2056, 0), new(2056, 4096)];

    private static VectorTileFeature Road(VectorPoint[] line, string category, long level, long? drawOrder)
    {
        var properties = new Dictionary<string, object>
        {
            ["vt_rdctg"] = category,
            ["vt_rnkwidth"] = "19.5m以上",
            ["vt_lvorder"] = level,
        };
        if (drawOrder is not null)
        {
            properties["vt_drworder"] = drawOrder.Value;
        }

        return new VectorTileFeature
        {
            GeometryType = VectorGeometryType.LineString,
            GeometryParts = [[.. line]],
            Properties = properties,
        };
    }

    private static SKBitmap RenderRoadCrossing(params VectorTileFeature[] roads)
    {
        using var style = new GsiVectorStyle();
        var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var tile = new VectorTile
        {
            Layers = [new VectorTileLayer { Name = "RdCL", Extent = 4096, Features = [.. roads] }],
        };

        const float tileScale = 256f / 4096f;
        canvas.Scale(tileScale, tileScale);

        // 線幅を 4 倍にして、casing の縁を画素の中心で判定できるようにする
        style.RenderLines(canvas, tile, tileScale / 4f, 14, CancellationToken.None);
        return bitmap;
    }

    [Fact]
    public void Annoレイヤーの実スキーマ注記が描画される()
    {
        using var style = new GsiVectorStyle();
        using var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var collisionGrid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, 256, 256);
        var matrix = SKMatrix.CreateScale(256f / 4096f, 256f / 4096f);

        var tile = new VectorTile
        {
            Layers =
            [
                new VectorTileLayer
                {
                    Name = "Anno",
                    Extent = 4096,
                    Features =
                    [
                        new VectorTileFeature
                        {
                            GeometryType = VectorGeometryType.Point,
                            GeometryParts = [[new(2048, 2048)]],
                            Properties = new Dictionary<string, object>
                            {
                                ["vt_text"] = "東京駅", // 現行 optimal_bvmap-v1 実データの属性名
                            },
                        },
                    ],
                },
            ],
        };

        style.RenderLabels(canvas, tile, matrix, 14, collisionGrid, viewport, CancellationToken.None);

        bool foundText = false;
        for (int y = 120; y <= 135; y++)
        {
            for (int x = 110; x <= 145; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    foundText = true;
                    break;
                }
            }

            if (foundText)
            {
                break;
            }
        }

        Assert.True(foundText, "vt_text による注記が描画されている必要があります。");
    }
}
