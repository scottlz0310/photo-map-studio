using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// OpenMapTiles スキーマの <c>housenumber</c> レイヤー（建物の番地）を、小さな灰色の文字で描画する。
/// </summary>
/// <remarks>
/// 地名・施設名・道路名より後に置き、先に置かれた注記と重なる番地は描かない。寸法は論理ピクセル。
/// </remarks>
internal static class OpenMapTilesHouseNumbers
{
    /// <summary>番地を描く最小のズーム。これより低いと、文字が密集して読めない。</summary>
    internal const int MinZoom = 17;

    /// <summary>描く番地の最大の文字数。施設名や階数が混じった長い値（例: <c>5 ○○タワー1F</c>）は、番地として読めないため描かない。</summary>
    internal const int MaxLength = 8;

    private const float FontSize = 9f;

    private static readonly SKColor TextColor = new(0x6E, 0x6E, 0x6E);

    /// <summary>描く番地の文字列。空・長すぎる値・空白を含む値は <see langword="null"/>。</summary>
    /// <param name="raw">地物の <c>housenumber</c> 属性。</param>
    internal static string? GetText(string? raw)
    {
        string? text = raw?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxLength || text.Any(char.IsWhiteSpace) ? null : text;
    }

    /// <summary>番地を描画する。</summary>
    internal static void Render(
        SKCanvas canvas,
        VectorTile tile,
        SKMatrix tileToCanvasMatrix,
        int zoom,
        LabelCollisionGrid collisionGrid,
        SKRect viewport,
        SKTypeface typeface,
        CancellationToken cancellationToken)
    {
        VectorTileLayer? layer = tile.GetLayer("housenumber");
        if (layer is null || zoom < MinZoom)
        {
            return;
        }

        using var font = new SKFont(typeface, FontSize);
        using var fill = new SKPaint { Color = TextColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var halo = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 2.5f, IsAntialias = true };
        foreach (VectorTileFeature feature in layer.Features)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (feature.GeometryType != VectorGeometryType.Point
                || feature.GeometryParts.Count == 0
                || feature.GeometryParts[0].Count == 0
                || GetText(feature.GetString("housenumber")) is not { } text)
            {
                continue;
            }

            VectorPoint point = feature.GeometryParts[0][0];
            SKPoint anchor = tileToCanvasMatrix.MapPoint(new SKPoint(point.X, point.Y));
            float halfWidth = font.MeasureText(text) / 2f;
            var bounds = new SKRect(anchor.X - halfWidth - 1f, anchor.Y - FontSize, anchor.X + halfWidth + 1f, anchor.Y + 2f);
            if (!collisionGrid.TryPlace([bounds], viewport))
            {
                continue;
            }

            canvas.DrawText(text, anchor.X, anchor.Y, SKTextAlign.Center, font, halo);
            canvas.DrawText(text, anchor.X, anchor.Y, SKTextAlign.Center, font, fill);
        }
    }
}
