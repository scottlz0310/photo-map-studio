using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public sealed class GsiVectorStyleTests
{
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
        style.RenderPolygons(canvas, tile, tileScale, 14);

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
        style.RenderLines(canvas, tile, tileScale, 14);

        // y = 128 付近に国道が描画されていること
        SKColor roadPixel = bitmap.GetPixel(128, 128);
        Assert.True(roadPixel.Alpha > 0);
    }

    [Theory]
    [InlineData("高速自動車国道等", "19.5m以上", GsiVectorStyle.GsiRoadKind.Highway, 5.2f)]
    [InlineData("国道", "5.5m-13m未満", GsiVectorStyle.GsiRoadKind.National, 3.2f)]
    [InlineData("主要道路", "3m-5.5m未満", GsiVectorStyle.GsiRoadKind.National, 3.2f)]
    [InlineData("都道府県道", "3m-5.5m未満", GsiVectorStyle.GsiRoadKind.Prefectural, 2.8f)]
    [InlineData("市区町村道等", "3m未満", GsiVectorStyle.GsiRoadKind.Minor, 1.5f)]
    [InlineData("その他", "不明", GsiVectorStyle.GsiRoadKind.Minor, 2.2f)]
    [InlineData("0", "2", GsiVectorStyle.GsiRoadKind.National, 3.2f)]
    public void 実スキーマ文字列属性による道路分類と幅員が正確に解決される(
        string rdCtg,
        string rnkWidth,
        GsiVectorStyle.GsiRoadKind expectedKind,
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

        Assert.Equal(expectedKind, kind);
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
        style.RenderLines(canvas, tile, tileScale, 14);

        // y = 128 付近の道路中心ピクセル色をサンプリング
        SKColor roadPixel = bitmap.GetPixel(128, 128);
        Assert.Equal(expectedR, roadPixel.Red);
        Assert.Equal(expectedG, roadPixel.Green);
        Assert.Equal(expectedB, roadPixel.Blue);
        Assert.Equal(255, roadPixel.Alpha);
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

        style.RenderLabels(canvas, tile, matrix, 14, collisionGrid, viewport);

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
