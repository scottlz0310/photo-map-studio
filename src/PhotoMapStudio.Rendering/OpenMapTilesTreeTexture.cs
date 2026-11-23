using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// 森（<c>landcover</c> の <c>wood</c>）に重ねる樹木の模様。小さな樹冠と幹を、ずらして並べて描く。
/// </summary>
/// <remarks>
/// 模様は、森の面でクリップして、格子上の点だけを 1 回の <c>DrawPoints</c> で描く。繰り返しパターンのシェーダーで面全体を塗る方式は、
/// 面の全画素を模様で塗り直すため、森が画面の大半を占める表示で描画時間が数倍になる（実測）。
/// 寸法は論理ピクセルで、描画先の拡大（<see cref="VectorViewRequest.PixelRatio"/>）に合わせて鮮明に描かれる。
/// </remarks>
internal sealed class OpenMapTilesTreeTexture
{
    /// <summary>模様を重ねる最小のズーム。これより低いと、樹冠が点の集まりになり、色を濁らせるだけになる。</summary>
    internal const int MinZoom = 14;

    /// <summary>樹木の格子の間隔（論理ピクセル）。</summary>
    internal const float Period = 24f;

    /// <summary>1 つの地物に描く樹木の最大数。範囲の異常な広がり（拡大しすぎなど）で、描画が長引かないようにする。</summary>
    internal const int MaxTrees = 20000;

    private const float CanopyRadius = 2.4f;
    private const float TrunkLength = 2f;

    // 2 本を対角にずらして置く（論理ピクセル。格子の 1 区画の中の位置）
    private static readonly (float X, float Y)[] Offsets = [(6f, 7f), (18f, 19f)];

    private static readonly SKColor Color = new(0x78, 0xA4, 0x66, 0x8C);

    /// <summary>
    /// 森の面 <paramref name="polygon"/> の内側に、樹木を描く。
    /// </summary>
    /// <param name="canvas">描画先（タイル座標系）。</param>
    /// <param name="polygon">森の面の輪郭（タイル座標）。</param>
    /// <param name="tileScale">タイル座標から（論理）ピクセルへの拡大率。</param>
    internal static void Draw(SKCanvas canvas, SKPath polygon, float tileScale)
    {
        // 画面の外には描かない（面は画面よりはるかに大きいことがある）
        SKRect area = SKRect.Intersect(polygon.Bounds, canvas.LocalClipBounds);
        if (area.IsEmpty)
        {
            return;
        }

        float period = Period / tileScale;
        int firstColumn = (int)Math.Floor(area.Left / period);
        int lastColumn = (int)Math.Floor(area.Right / period);
        int firstRow = (int)Math.Floor(area.Top / period);
        int lastRow = (int)Math.Floor(area.Bottom / period);
        long count = (lastColumn - firstColumn + 1L) * (lastRow - firstRow + 1L) * Offsets.Length;
        if (count > MaxTrees)
        {
            return;
        }

        var canopies = new SKPoint[count];
        var trunks = new SKPoint[count * 2];
        int n = 0;
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                foreach ((float x, float y) in Offsets)
                {
                    float cx = (column * period) + (x / tileScale);
                    float cy = (row * period) + (y / tileScale);
                    canopies[n] = new SKPoint(cx, cy);
                    trunks[2 * n] = new SKPoint(cx, cy + (CanopyRadius / tileScale));
                    trunks[(2 * n) + 1] = new SKPoint(cx, cy + ((CanopyRadius + TrunkLength) / tileScale));
                    n++;
                }
            }
        }

        // 同じ色の半透明な 2 つの図形が重ならないよう、幹と樹冠は別々に描く（幹は樹冠の下端から始まる）
        using var canopyPaint = new SKPaint { Color = Color, Style = SKPaintStyle.Stroke, StrokeWidth = 2f * CanopyRadius / tileScale, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        using var trunkPaint = new SKPaint { Color = Color, Style = SKPaintStyle.Stroke, StrokeWidth = 1f / tileScale, IsAntialias = true };
        canvas.Save();
        try
        {
            canvas.ClipPath(polygon, SKClipOperation.Intersect, false);
            canvas.DrawPoints(SKPointMode.Lines, trunks, trunkPaint);
            canvas.DrawPoints(SKPointMode.Points, canopies, canopyPaint);
        }
        finally
        {
            canvas.Restore();
        }
    }
}
