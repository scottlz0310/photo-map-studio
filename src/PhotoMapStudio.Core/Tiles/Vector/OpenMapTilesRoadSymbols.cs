using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 経路番号の盾（国道・都道府県道・高速）の種別。
/// </summary>
internal enum RouteShieldKind
{
    /// <summary>盾を描かない。</summary>
    None,

    /// <summary>高速自動車国道・都市高速（緑の角丸）。</summary>
    Expressway,

    /// <summary>国道（青の逆三角形）。</summary>
    National,

    /// <summary>都道府県道（白地の五角形）。</summary>
    Prefectural,
}

/// <summary>
/// OpenMapTiles スキーマの <c>transportation_name</c> レイヤーから、経路番号の盾と道路名注記を描画する。
/// </summary>
/// <remarks>
/// 道路名は折れ線に沿わせず、道路の中央に水平に配置する（折れ線追従は別途検討）。
/// </remarks>
internal static class OpenMapTilesRoadSymbols
{
    private const string LayerName = "transportation_name";
    private const int ShieldMinZoom = 12;
    private const float ShieldHeight = 14f;
    private const float ShieldMinWidth = 16f;
    private const float ShieldPadding = 4f;
    private const float NameAscent = 9f;
    private const float NameDescent = 3f;

    /// <summary>
    /// 道路種別と経路網から盾の種別を決める。
    /// </summary>
    /// <remarks>
    /// OpenFreeMap の日本の道路は経路網（<c>network</c>）が地域区分を持たない <c>road</c> であることが多いため、
    /// 経路網が <c>jp-*</c> のときはそれを使い、それ以外は道路種別（class）で判定する
    /// （幹線・主要道路は国道、二次・三次道路は都道府県道とみなす。実際の路線種別と異なる場合がある）。
    /// </remarks>
    /// <param name="roadClass">道路種別（<c>class</c>）。</param>
    /// <param name="network">経路網（<c>network</c>）。</param>
    /// <returns>盾の種別。</returns>
    internal static RouteShieldKind GetShieldKind(string? roadClass, string? network)
    {
        if (string.Equals(network, "jp-expressway", StringComparison.OrdinalIgnoreCase))
        {
            return RouteShieldKind.Expressway;
        }

        if (string.Equals(network, "jp-national", StringComparison.OrdinalIgnoreCase))
        {
            return RouteShieldKind.National;
        }

        if (string.Equals(network, "jp-prefectural", StringComparison.OrdinalIgnoreCase))
        {
            return RouteShieldKind.Prefectural;
        }

        return roadClass switch
        {
            "motorway" => RouteShieldKind.Expressway,
            "trunk" or "primary" => RouteShieldKind.National,
            "secondary" or "tertiary" => RouteShieldKind.Prefectural,
            _ => RouteShieldKind.None,
        };
    }

    /// <summary>
    /// 盾に表示する経路番号を取り出す。複数の路線が重なる道路（例: "1;8"）は先頭の番号を使う。
    /// </summary>
    /// <param name="reference">属性 <c>ref</c> の値。</param>
    /// <returns>表示する番号。表示できない場合は <see langword="null"/>。</returns>
    internal static string? GetShieldText(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        string first = reference.Split(';')[0].Trim();
        return first.Length is > 0 and <= 5 ? first : null;
    }

    /// <summary>
    /// 経路番号の盾を描画する。
    /// </summary>
    internal static void RenderShields(
        SKCanvas canvas,
        VectorTile tile,
        SKMatrix tileToCanvasMatrix,
        int zoom,
        LabelCollisionGrid collisionGrid,
        SKRect viewport,
        SKFont shieldFont)
    {
        VectorTileLayer? layer = tile.GetLayer(LayerName);
        if (zoom < ShieldMinZoom || layer is null)
        {
            return;
        }

        // 1 つのタイルの中では、同じ路線（種別と番号）の盾は最も長い区間に 1 つだけ置く
        var candidates = new Dictionary<(RouteShieldKind Kind, string Text), (SKPoint Anchor, float Length)>();
        for (int i = 0; i < layer.Features.Count; i++)
        {
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            RouteShieldKind kind = GetShieldKind(feature.GetString("class"), feature.GetString("network"));
            string? text = GetShieldText(feature.GetString("ref"));
            if (kind == RouteShieldKind.None || text is null)
            {
                continue;
            }

            // 盾が画面からはみ出さない範囲の、見えている区間だけで配置点を選ぶ（過拡大でタイルが画面より大きい場合に備える）
            float shieldWidth = Math.Max(ShieldMinWidth, shieldFont.MeasureText(text) + (ShieldPadding * 2f));
            SKRect placeable = Inset(viewport, shieldWidth / 2f, ShieldHeight / 2f, shieldWidth / 2f, ShieldHeight / 2f);
            if (!TryGetVisibleMidpoint(feature, tileToCanvasMatrix, placeable, out SKPoint anchor, out float length)
                || length < ShieldMinWidth * 1.5f)
            {
                continue;
            }

            var key = (kind, text);
            if (!candidates.TryGetValue(key, out var existing) || length > existing.Length)
            {
                candidates[key] = (anchor, length);
            }
        }

        foreach (var ((kind, text), (anchor, _)) in candidates
            .OrderBy(static c => (int)c.Key.Kind)
            .ThenByDescending(static c => c.Value.Length))
        {
            float width = Math.Max(ShieldMinWidth, shieldFont.MeasureText(text) + (ShieldPadding * 2f));
            var bounds = SKRect.Create(anchor.X - (width / 2f), anchor.Y - (ShieldHeight / 2f), width, ShieldHeight);
            if (collisionGrid.TryPlace(bounds, viewport))
            {
                DrawShield(canvas, kind, text, bounds, shieldFont);
            }
        }
    }

    /// <summary>
    /// 道路名を描画する（折れ線には沿わせず、道路の中央に水平に置く）。
    /// </summary>
    internal static void RenderNames(
        SKCanvas canvas,
        VectorTile tile,
        SKMatrix tileToCanvasMatrix,
        int zoom,
        LabelCollisionGrid collisionGrid,
        SKRect viewport,
        SKFont font,
        SKPaint fillPaint,
        SKPaint haloPaint)
    {
        VectorTileLayer? layer = tile.GetLayer(LayerName);
        if (layer is null)
        {
            return;
        }

        // 同じ名前は 1 つのタイルの中で最も長い区間に 1 つだけ置き、主要な道路から先に配置する
        var candidates = new Dictionary<string, (SKPoint Anchor, float Length, int Rank)>(StringComparer.Ordinal);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            int rank = GetNameRank(feature.GetString("class"), zoom);
            string? name = feature.GetString("name:ja") ?? feature.GetString("name");
            if (rank < 0 || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // 道路より長い名前は、隣の道路に重なって読みにくいため置かない（画面内に見えている長さで判断する）
            float textWidth = font.MeasureText(name);
            if (!TryGetVisibleMidpoint(feature, tileToCanvasMatrix, viewport, out _, out float visibleLength)
                || visibleLength < textWidth * 0.9f)
            {
                continue;
            }

            // 配置点は、文字が画面からはみ出さない範囲の、見えている区間の中央にする
            SKRect placeable = Inset(viewport, textWidth / 2f, NameAscent, textWidth / 2f, NameDescent);
            if (!TryGetVisibleMidpoint(feature, tileToCanvasMatrix, placeable, out SKPoint anchor, out _))
            {
                continue;
            }

            if (!candidates.TryGetValue(name, out var existing) || visibleLength > existing.Length)
            {
                candidates[name] = (anchor, visibleLength, rank);
            }
        }

        foreach (var (name, (anchor, _, _)) in candidates
            .OrderByDescending(static c => c.Value.Rank)
            .ThenByDescending(static c => c.Value.Length))
        {
            float textWidth = font.MeasureText(name);
            var bounds = new SKRect(anchor.X - (textWidth / 2f), anchor.Y - NameAscent, anchor.X + (textWidth / 2f), anchor.Y + NameDescent);
            if (collisionGrid.TryPlace(bounds, viewport))
            {
                canvas.DrawText(name, bounds.Left, anchor.Y, SKTextAlign.Left, font, haloPaint);
                canvas.DrawText(name, bounds.Left, anchor.Y, SKTextAlign.Left, font, fillPaint);
            }
        }
    }

    /// <summary>
    /// 道路種別から道路名の優先度を決める。ズームに対して表示しない種別は負の値を返す。
    /// </summary>
    private static int GetNameRank(string? roadClass, int zoom) => roadClass switch
    {
        "motorway" or "trunk" => zoom >= 12 ? 5 : -1,
        "primary" => zoom >= 12 ? 4 : -1,
        "secondary" => zoom >= 13 ? 3 : -1,
        "tertiary" => zoom >= 14 ? 2 : -1,
        "minor" or "service" => zoom >= 15 ? 1 : -1,
        _ => -1,
    };

    private static SKRect Inset(SKRect rect, float left, float top, float right, float bottom)
        => new(rect.Left + left, rect.Top + top, rect.Right - right, rect.Bottom - bottom);

    /// <summary>
    /// 地物のうち、指定した矩形の内側に見えている最も長い連続区間を選び、その中点（画面座標）と長さを求める。
    /// </summary>
    /// <remarks>
    /// 区間全体の中点を使うと、過拡大でタイルが画面より大きいとき（OpenFreeMap は最大ズーム 14 のタイルを拡大する）、
    /// 画面を横切る道路でも中点が画面外になり、盾・道路名が消える。
    /// </remarks>
    private static bool TryGetVisibleMidpoint(VectorTileFeature feature, SKMatrix tileToCanvasMatrix, SKRect clip, out SKPoint midpoint, out float length)
    {
        midpoint = default;
        length = 0f;
        if (clip.Width <= 0f || clip.Height <= 0f)
        {
            return false;
        }

        List<SKPoint>? best = null;
        float bestLength = 0f;

        for (int p = 0; p < feature.GeometryParts.Count; p++)
        {
            IReadOnlyList<VectorPoint> line = feature.GeometryParts[p];
            if (line.Count < 2)
            {
                continue;
            }

            List<SKPoint>? run = null;
            float runLength = 0f;
            bool endedAtVertex = false;
            SKPoint previous = tileToCanvasMatrix.MapPoint(new SKPoint(line[0].X, line[0].Y));

            for (int i = 1; i < line.Count; i++)
            {
                SKPoint current = tileToCanvasMatrix.MapPoint(new SKPoint(line[i].X, line[i].Y));

                if (!TryClipSegment(previous, current, clip, out float t0, out float t1))
                {
                    Close(ref run, ref runLength);
                }
                else
                {
                    SKPoint a = Lerp(previous, current, t0);
                    SKPoint b = Lerp(previous, current, t1);

                    // 前の線分が頂点まで見えていて、この線分も頂点から見えているときだけ同じ区間として続ける
                    if (run is null || !endedAtVertex || t0 > 1e-4f)
                    {
                        Close(ref run, ref runLength);
                        run = [a];
                    }

                    run.Add(b);
                    runLength += SKPoint.Distance(a, b);
                    endedAtVertex = t1 >= 1f - 1e-4f;

                    if (!endedAtVertex)
                    {
                        Close(ref run, ref runLength);
                    }
                }

                previous = current;
            }

            Close(ref run, ref runLength);

            void Close(ref List<SKPoint>? currentRun, ref float currentLength)
            {
                if (currentRun is not null && currentLength > bestLength)
                {
                    best = currentRun;
                    bestLength = currentLength;
                }

                currentRun = null;
                currentLength = 0f;
            }
        }

        if (best is null || bestLength <= 0f)
        {
            return false;
        }

        float half = bestLength / 2f;
        float walked = 0f;
        for (int i = 1; i < best.Count; i++)
        {
            float segment = SKPoint.Distance(best[i - 1], best[i]);
            if (walked + segment >= half && segment > 0f)
            {
                midpoint = Lerp(best[i - 1], best[i], (half - walked) / segment);
                length = bestLength;
                return true;
            }

            walked += segment;
        }

        return false;
    }

    private static SKPoint Lerp(SKPoint from, SKPoint to, float t)
        => new(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));

    /// <summary>
    /// 線分のうち矩形の内側にある範囲を、線分上の位置（0〜1）で求める（Liang–Barsky 法）。
    /// </summary>
    private static bool TryClipSegment(SKPoint from, SKPoint to, SKRect rect, out float t0, out float t1)
    {
        t0 = 0f;
        t1 = 1f;
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;

        return Clip(-dx, from.X - rect.Left, ref t0, ref t1)
            && Clip(dx, rect.Right - from.X, ref t0, ref t1)
            && Clip(-dy, from.Y - rect.Top, ref t0, ref t1)
            && Clip(dy, rect.Bottom - from.Y, ref t0, ref t1)
            && t1 > t0;

        static bool Clip(float p, float q, ref float lower, ref float upper)
        {
            if (Math.Abs(p) < 1e-9f)
            {
                return q >= 0f;
            }

            float r = q / p;
            if (p < 0f)
            {
                if (r > upper)
                {
                    return false;
                }

                lower = Math.Max(lower, r);
            }
            else
            {
                if (r < lower)
                {
                    return false;
                }

                upper = Math.Min(upper, r);
            }

            return true;
        }
    }

    private static void DrawShield(SKCanvas canvas, RouteShieldKind kind, string text, SKRect bounds, SKFont font)
    {
        (SKColor fill, SKColor outline, SKColor textColor) = kind switch
        {
            RouteShieldKind.Expressway => (new SKColor(0x0A, 0x8A, 0x3C), SKColors.White, SKColors.White),
            RouteShieldKind.National => (new SKColor(0x00, 0x62, 0xB8), SKColors.White, SKColors.White),
            _ => (SKColors.White, new SKColor(0x33, 0x33, 0x33), new SKColor(0x22, 0x22, 0x22)),
        };

        using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var outlinePaint = new SKPaint
        {
            Color = outline,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        using var builder = new SKPathBuilder();
        float cx = bounds.MidX;
        switch (kind)
        {
            case RouteShieldKind.National:
                // 逆三角形（角を丸めるため、縁も同色の太線で描く）
                builder.MoveTo(bounds.Left + 1f, bounds.Top);
                builder.LineTo(bounds.Right - 1f, bounds.Top);
                builder.LineTo(cx, bounds.Bottom + 1f);
                builder.Close();
                break;
            case RouteShieldKind.Prefectural:
                // 五角形（下向きの先端を持つ）
                builder.MoveTo(bounds.Left, bounds.Top);
                builder.LineTo(bounds.Right, bounds.Top);
                builder.LineTo(bounds.Right, bounds.Top + (bounds.Height * 0.62f));
                builder.LineTo(cx, bounds.Bottom);
                builder.LineTo(bounds.Left, bounds.Top + (bounds.Height * 0.62f));
                builder.Close();
                break;
            default:
                using (var roundRect = new SKRoundRect(bounds, 4f, 4f))
                {
                    builder.AddRoundRect(roundRect);
                }

                break;
        }

        using SKPath path = builder.Detach();
        canvas.DrawPath(path, fillPaint);
        canvas.DrawPath(path, outlinePaint);

        using var textPaint = new SKPaint { Color = textColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        float textWidth = font.MeasureText(text);
        float baseline = bounds.Top + (kind == RouteShieldKind.National ? 9.5f : 10.5f);
        canvas.DrawText(text, cx - (textWidth / 2f), baseline, SKTextAlign.Left, font, textPaint);
    }
}
