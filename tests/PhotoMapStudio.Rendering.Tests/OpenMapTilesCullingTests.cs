using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

/// <summary>
/// 表示範囲の外にある地物を描かない最適化（Cull・破線の切り詰め）が、出力を変えないことを確認する。
/// 同じ小さなキャンバスへ、最適化あり（既定）と、全ての地物を全長で描く基準の経路で描いて比べる。
/// </summary>
public class OpenMapTilesCullingTests
{
    private const float TileScale = 0.25f;
    private const int Zoom = 19;

    private static readonly (int X, int Y)[] Winding =
    [
        (100, 3900), (900, 2100), (2100, 2500), (3200, 600), (3900, 1500),
    ];

    private static readonly (int X, int Y)[] Straight = [(0, 1800), (4096, 2300)];

    // 実線・鉄道は、範囲外の地物を描かないだけなので完全に一致する。
    // 破線の小道は、範囲と交わる区間だけを破線の位相を合わせて描く。基準の経路は Skia が距離を float で累積した位相、
    // こちらは double で求めた位相のため、破線の縁の半透明画素が 1〜2 だけずれ得る（見た目は同じ）。
    [Theory]
    [InlineData("path", 300, 300, 4)]
    [InlineData("path", 0, 0, 4)]
    [InlineData("path", 700, 100, 4)]
    [InlineData("footway", 250, 400, 4)]
    [InlineData("primary", 300, 300, 0)]
    [InlineData("primary", 0, 0, 0)]
    [InlineData("rail", 300, 300, 0)]
    [InlineData("rail", 700, 100, 0)]
    public void 最適化した描画は基準の経路と同じ画素になる(string roadClass, int offsetX, int offsetY, int allowedDifference)
    {
        VectorTile tile = Decode(MvtTestTile.CreateTransportation((roadClass, Winding), (roadClass, Straight)));
        using var optimized = new OpenMapTilesStyle();
        using var reference = new OpenMapTilesStyle(cullToClip: false);

        using SKBitmap expectedBitmap = Render(reference, tile, offsetX, offsetY);
        using SKBitmap actualBitmap = Render(optimized, tile, offsetX, offsetY);

        int different = 0;
        int maxDifference = 0;
        for (int y = 0; y < 200; y++)
        {
            for (int x = 0; x < 200; x++)
            {
                SKColor expected = expectedBitmap.GetPixel(x, y);
                SKColor actual = actualBitmap.GetPixel(x, y);
                int difference = Math.Max(
                    Math.Abs(expected.Red - actual.Red),
                    Math.Max(Math.Abs(expected.Green - actual.Green), Math.Abs(expected.Blue - actual.Blue)));
                if (difference > 0)
                {
                    different++;
                    maxDifference = Math.Max(maxDifference, difference);
                }
            }
        }

        Assert.True(maxDifference <= allowedDifference, $"{different} 画素が異なる（最大差 {maxDifference}、許容 {allowedDifference}）");
        // 許容する場合でも、ずれるのは破線の縁の画素だけ（画像全体の 0.1% 未満）
        Assert.True(different < 40, $"{different} 画素が異なる");
    }

    private static SKBitmap Render(OpenMapTilesStyle style, VectorTile tile, int offsetX, int offsetY)
    {
        var bitmap = new SKBitmap(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(style.BackgroundColor);
        canvas.Translate(-offsetX, -offsetY);
        canvas.Scale(TileScale, TileScale);
        style.RenderLines(canvas, tile, TileScale, Zoom, CancellationToken.None);
        return bitmap;
    }

    private static VectorTile Decode(byte[] data)
    {
        Assert.True(VectorTileDecoder.TryDecode(data, out VectorTile tile));
        return tile;
    }
}
