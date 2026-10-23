using System.Reflection;

using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

/// <summary>
/// DI でシングルトン登録されるスタイルが、描画で共有ペイントを書き換えないことを確認する。
/// プレビューと一括生成が同時に描画しても線幅が混ざらないための前提。
/// </summary>
public sealed class VectorStyleSharedStateTests
{
    private const float ScaleA = 256f / 4096f;
    private const float ScaleB = 1024f / 4096f;

    [Theory]
    [InlineData(typeof(GsiVectorStyle))]
    [InlineData(typeof(OpenMapTilesStyle))]
    public void 線の描画で共有ペイントの線幅を書き換えない(Type styleType)
    {
        using var style = (IDisposable)Activator.CreateInstance(styleType)!;
        var vectorStyle = (IVectorMapStyle)style;
        VectorTile tile = CreateLineTile(styleType);
        Dictionary<string, float> before = SnapshotStrokeWidths(style);

        RenderLines(vectorStyle, tile, ScaleA, 16);
        RenderLines(vectorStyle, tile, ScaleB, 16);

        Assert.Equal(before, SnapshotStrokeWidths(style));
    }

    [Theory]
    [InlineData(typeof(GsiVectorStyle))]
    [InlineData(typeof(OpenMapTilesStyle))]
    public async Task 異なる縮尺で並行して描画しても単独で描画した結果と一致する(Type styleType)
    {
        using var style = (IDisposable)Activator.CreateInstance(styleType)!;
        var vectorStyle = (IVectorMapStyle)style;
        VectorTile tile = CreateLineTile(styleType);
        byte[] expectedA = RenderLines(vectorStyle, tile, ScaleA, 16);
        byte[] expectedB = RenderLines(vectorStyle, tile, ScaleB, 16);

        var tasks = Enumerable.Range(0, 64)
            .Select(i => Task.Run(() => i % 2 == 0
                ? (Scale: ScaleA, Bytes: RenderLines(vectorStyle, tile, ScaleA, 16))
                : (Scale: ScaleB, Bytes: RenderLines(vectorStyle, tile, ScaleB, 16))))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        foreach (var (scale, bytes) in results)
        {
            Assert.True(
                bytes.AsSpan().SequenceEqual(scale == ScaleA ? expectedA : expectedB),
                $"縮尺 {scale} の並行描画の結果が単独描画と異なる");
        }
    }

    private static Dictionary<string, float> SnapshotStrokeWidths(object style)
        => style.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(SKPaint))
            .ToDictionary(f => f.Name, f => ((SKPaint)f.GetValue(style)!).StrokeWidth);

    private static byte[] RenderLines(IVectorMapStyle style, VectorTile tile, float tileScale, int zoom)
    {
        using var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(tileScale, tileScale);
            style.RenderLines(canvas, tile, tileScale, zoom);
        }

        return bitmap.Bytes;
    }

    private static VectorTile CreateLineTile(Type styleType)
        => styleType == typeof(GsiVectorStyle)
            ? new VectorTile
            {
                Layers =
                [
                    Line("Cntr", 400, null),
                    Line("WL", 800, null),
                    Line("RvrCL", 1200, null),
                    Line("AdmBdry", 1600, null),
                    Line("RailCL", 2000, null),
                    Line("RdEdg", 2400, null),
                    Line("RdCL", 2800, new Dictionary<string, object> { ["rdCtg"] = 0L, ["rnkWidth"] = 2L }),
                    Line("RdCL", 3200, new Dictionary<string, object> { ["rdCtg"] = 3L, ["rnkWidth"] = 0L }),
                ],
            }
            : new VectorTile
            {
                Layers =
                [
                    Line("transportation", 1000, new Dictionary<string, object> { ["class"] = "rail" }),
                    Line("transportation", 2000, new Dictionary<string, object> { ["class"] = "motorway" }),
                    Line("railway", 3000, null),
                ],
            };

    private static VectorTileLayer Line(string name, int y, Dictionary<string, object>? properties)
        => new()
        {
            Name = name,
            Extent = 4096,
            Features =
            [
                new VectorTileFeature
                {
                    GeometryType = VectorGeometryType.LineString,
                    GeometryParts = [[new(0, y), new(4096, y)]],
                    Properties = properties ?? [],
                },
            ],
        };
}
