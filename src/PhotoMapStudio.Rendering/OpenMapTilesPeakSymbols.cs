using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// OpenMapTiles スキーマの <c>mountain_peak</c> レイヤーから、山頂の記号（三角）・山名・標高を描画する。
/// </summary>
/// <remarks>
/// 寸法は論理ピクセル（<see cref="OpenMapTilesPoiSymbols"/> と同じ。キャンバスの拡大で物理ピクセルへ変わる）。
/// </remarks>
internal static class OpenMapTilesPeakSymbols
{
    private const string LayerName = "mountain_peak";
    private const int MinZoom = 11;
    private const float TriangleWidth = 9f;
    private const float TriangleHeight = 8f;
    private const float NameFontSize = 11f;
    private const float ElevationFontSize = 10f;
    private const float RepeatDistance = 220f;

    private static readonly SKColor PeakColor = new(0x8C, 0x5A, 0x34);
    private static readonly SKColor VolcanoColor = new(0xC0, 0x30, 0x2A);

    /// <summary>
    /// 山頂の記号を描く種類か。<c>mountain_peak</c> には、峠・鞍部（<c>saddle</c>）や崖（<c>cliff</c>）も入るため、
    /// 山頂（<c>peak</c>）と火山（<c>volcano</c>）だけにする。
    /// </summary>
    /// <param name="peakClass">地物の <c>class</c> 属性。</param>
    internal static bool IsPeak(string? peakClass) => peakClass is "peak" or "volcano";

    /// <summary>
    /// 山頂を、ズームに応じて描くか。OpenMapTiles の <c>rank</c> は、タイル内の標高の高い順（1 が最高）。
    /// 低いズームでは、タイルに多数の山頂が入るため、上位だけを描いて記号と注記が密集しないようにする。
    /// </summary>
    /// <param name="zoom">スタイルのズーム。</param>
    /// <param name="rank">地物の <c>rank</c>。無い場合は <see langword="null"/>（描く対象から外す）。</param>
    internal static bool ShouldShow(int zoom, long? rank)
    {
        if (zoom < MinZoom || rank is null)
        {
            return false;
        }

        long maxRank = zoom switch
        {
            11 => 2,
            12 => 3,
            13 => 6,
            _ => long.MaxValue,
        };
        return rank <= maxRank;
    }

    /// <summary>標高の表示文字列（例: <c>2291 m</c>）。標高が無い・負・数値でない場合は <see langword="null"/>。</summary>
    /// <param name="elevation">地物の <c>ele</c> 属性（メートル）。</param>
    internal static string? FormatElevation(string? elevation)
        => long.TryParse(elevation, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long meters)
            ? $"{meters} m"
            : null;

    /// <summary>山頂の種類の色。火山は赤、それ以外の山頂は茶色。</summary>
    internal static SKColor GetColor(string? peakClass) => peakClass == "volcano" ? VolcanoColor : PeakColor;

    /// <summary>
    /// 山頂を描画する。名前のない山頂（<c>class=cliff</c> など）は描かない。
    /// </summary>
    internal static void Render(
        SKCanvas canvas,
        VectorTile tile,
        SKMatrix tileToCanvasMatrix,
        int zoom,
        LabelCollisionGrid collisionGrid,
        SKRect viewport,
        SKTypeface typeface,
        SKPaint haloPaint,
        CancellationToken cancellationToken)
    {
        VectorTileLayer? layer = tile.GetLayer(LayerName);
        if (layer is null || zoom < MinZoom)
        {
            return;
        }

        using var nameFont = new SKFont(typeface, NameFontSize);
        using var elevationFont = new SKFont(typeface, ElevationFontSize);
        foreach (VectorTileFeature feature in layer.Features.OrderBy(f => f.GetInt64("rank") ?? long.MaxValue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? name = feature.GetString("name:ja") ?? feature.GetString("name");
            if (feature.GeometryType != VectorGeometryType.Point
                || feature.GeometryParts.Count == 0
                || feature.GeometryParts[0].Count == 0
                || string.IsNullOrWhiteSpace(name)
                || !IsPeak(feature.GetString("class"))
                || !ShouldShow(zoom, feature.GetInt64("rank")))
            {
                continue;
            }

            VectorPoint point = feature.GeometryParts[0][0];
            SKPoint anchor = tileToCanvasMatrix.MapPoint(new SKPoint(point.X, point.Y));
            string? elevation = FormatElevation(feature.GetString("ele"));
            float nameBaseline = anchor.Y + (TriangleHeight / 2f) + NameFontSize;
            float elevationBaseline = nameBaseline + ElevationFontSize + 1f;
            float textHalfWidth = Math.Max(nameFont.MeasureText(name), elevation is null ? 0f : elevationFont.MeasureText(elevation)) / 2f;
            float halfWidth = Math.Max(textHalfWidth, TriangleWidth / 2f);
            float bottom = (elevation is null ? nameBaseline : elevationBaseline) + 3f;
            var bounds = new SKRect(anchor.X - halfWidth - 2f, anchor.Y - (TriangleHeight / 2f) - 2f, anchor.X + halfWidth + 2f, bottom);
            if (!collisionGrid.TryPlace([bounds], viewport, "peak:" + name, RepeatDistance))
            {
                continue;
            }

            SKColor color = GetColor(feature.GetString("class"));
            using var fill = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var outline = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true };
            using var builder = new SKPathBuilder();
            builder.MoveTo(anchor.X, anchor.Y - (TriangleHeight / 2f));
            builder.LineTo(anchor.X + (TriangleWidth / 2f), anchor.Y + (TriangleHeight / 2f));
            builder.LineTo(anchor.X - (TriangleWidth / 2f), anchor.Y + (TriangleHeight / 2f));
            builder.Close();
            using SKPath triangle = builder.Detach();
            canvas.DrawPath(triangle, outline);
            canvas.DrawPath(triangle, fill);

            canvas.DrawText(name, anchor.X, nameBaseline, SKTextAlign.Center, nameFont, haloPaint);
            canvas.DrawText(name, anchor.X, nameBaseline, SKTextAlign.Center, nameFont, fill);
            if (elevation is not null)
            {
                canvas.DrawText(elevation, anchor.X, elevationBaseline, SKTextAlign.Center, elevationFont, haloPaint);
                canvas.DrawText(elevation, anchor.X, elevationBaseline, SKTextAlign.Center, elevationFont, fill);
            }
        }
    }
}
