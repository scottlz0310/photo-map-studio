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

        // ズーム14で描画（建物も描画対象）
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

        // y = 128 (2048 * 256 / 4096) 付近に国道が描画されていること
        SKColor roadPixel = bitmap.GetPixel(128, 128);
        Assert.True(roadPixel.Alpha > 0);
    }

    [Fact]
    public void Annoレイヤーの漢字注記が衝突判定を経て描画される()
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
                                ["knj"] = "東京",
                            },
                        },
                    ],
                },
            ],
        };

        style.RenderLabels(canvas, tile, matrix, 14, collisionGrid, viewport);

        // 中央 (128, 128) 付近にテキスト描画ピクセルが存在すること
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

        Assert.True(foundText, "Anno レイヤーの注記が描画されている必要があります。");
    }
}
