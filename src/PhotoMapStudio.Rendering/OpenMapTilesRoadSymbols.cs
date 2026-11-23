using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

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
/// 道路名は画面内の折れ線に沿わせ、短い区間・急カーブ・重なりを避ける。
/// </remarks>
internal static class OpenMapTilesRoadSymbols
{
    private const string LayerName = "transportation_name";
    private const int ShieldMinZoom = 12;
    private const float ShieldHeight = 14f;
    private const float ShieldMinWidth = 16f;
    private const float ShieldPadding = 4f;

    /// <summary>OpenMapTiles が路線関係ごとに持つ属性 <c>route_N_network</c>・<c>route_N_ref</c> の N の最大。</summary>
    private const int MaxRouteCount = 4;

    /// <summary>
    /// 経路網の名前から盾の種別を決める。
    /// </summary>
    /// <remarks>
    /// OSM の路線関係（route=road）の <c>network</c> で決める。国道は <c>JP:national</c>、都道府県道は <c>JP:prefectural</c>
    /// （<c>JP:prefectural:tottori</c> のように地域名が続くものを含む）、高速は <c>JP:E</c> と <c>JP:expressway*</c>。
    /// 市道（<c>JP:municipal*</c>・<c>JP:city*</c>）や旧街道などは盾の対象にしない。
    /// Protomaps 系のタイルが <c>network</c> に持つ <c>jp-expressway</c>・<c>jp-national</c>・<c>jp-prefectural</c> も同じ種別として扱う。
    /// </remarks>
    /// <param name="network">経路網の名前（<c>network</c> または <c>route_N_network</c>）。</param>
    /// <returns>盾の種別。対象外は <see cref="RouteShieldKind.None"/>。</returns>
    internal static RouteShieldKind GetShieldKind(string? network)
    {
        if (string.IsNullOrEmpty(network))
        {
            return RouteShieldKind.None;
        }

        if (network.Equals("JP:E", StringComparison.OrdinalIgnoreCase)
            || network.Equals("jp-expressway", StringComparison.OrdinalIgnoreCase)
            || IsNetwork(network, "JP:expressway"))
        {
            return RouteShieldKind.Expressway;
        }

        if (network.Equals("jp-national", StringComparison.OrdinalIgnoreCase) || IsNetwork(network, "JP:national"))
        {
            return RouteShieldKind.National;
        }

        if (network.Equals("jp-prefectural", StringComparison.OrdinalIgnoreCase) || IsNetwork(network, "JP:prefectural"))
        {
            return RouteShieldKind.Prefectural;
        }

        return RouteShieldKind.None;
    }

    /// <summary>
    /// 道路が属する路線から、盾の種別と表示する番号を決める。
    /// </summary>
    /// <remarks>
    /// OpenFreeMap の日本の道路は <c>network</c> が <c>road</c> で、国道・都道府県道の区別は路線関係ごとの
    /// <c>route_N_network</c>・<c>route_N_ref</c> に入る。道路種別（<c>class</c>）からの推定は、国道の精度が低く
    /// （<c>primary</c> の多くは都道府県道、大都市では市道）誤った盾が付くため行わない。路線関係を持たない道路には盾を描かない。
    /// 複数の路線を持つ道路は、高速、国道、都道府県道の順に優先し、同じ種別では先の路線を使う。
    /// 番号は、選んだ路線の <c>route_N_ref</c> から取り出す（<c>network</c>・<c>ref</c> の組も同様に候補にする）。
    /// </remarks>
    /// <param name="feature"><c>transportation_name</c> レイヤーの地物。</param>
    /// <returns>盾の種別と番号。盾を描かない場合は <see cref="RouteShieldKind.None"/> と <see langword="null"/>。</returns>
    internal static (RouteShieldKind Kind, string? Text) GetRouteShield(VectorTileFeature feature)
    {
        (RouteShieldKind Kind, string? Text) best = (RouteShieldKind.None, null);
        Consider(feature.GetString("network"), feature.GetString("ref"));
        for (int i = 1; i <= MaxRouteCount; i++)
        {
            Consider(feature.GetString($"route_{i}_network"), feature.GetString($"route_{i}_ref"));
        }

        return best;

        void Consider(string? network, string? reference)
        {
            RouteShieldKind kind = GetShieldKind(network);
            string? text = GetShieldText(reference);

            // 列挙値は高速 < 国道 < 都道府県道 の優先順。同順位では先に見つけた路線を残す
            if (kind != RouteShieldKind.None && text is not null && (best.Kind == RouteShieldKind.None || kind < best.Kind))
            {
                best = (kind, text);
            }
        }
    }

    /// <summary>経路網の名前が、指定した名前そのもの、またはその下位（<c>名前:地域</c>）か。</summary>
    private static bool IsNetwork(string network, string name)
        => network.Equals(name, StringComparison.OrdinalIgnoreCase)
        || network.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 盾に表示する経路番号を取り出す。複数の路線が重なる道路（例: "1;8"）は先頭の番号を使う。
    /// </summary>
    /// <param name="reference">属性 <c>ref</c> または <c>route_N_ref</c> の値。</param>
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
        SKFont shieldFont, CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            (RouteShieldKind kind, string? text) = GetRouteShield(feature);
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
            if (collisionGrid.TryPlace([bounds], viewport, $"shield:{kind}:{text}", repeatDistance: 260f))
            {
                DrawShield(canvas, kind, text, bounds, shieldFont);
            }
        }
    }

    /// <summary>細街路を含む道路名を、ズームに応じて道路に沿って描画する。</summary>
    internal static void RenderNames(
        SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport, SKFont font, SKPaint fillPaint, SKPaint haloPaint, CancellationToken cancellationToken)
    {
        VectorTileLayer? layer = tile.GetLayer(LayerName);
        if (layer is null) { return; }
        foreach (VectorTileFeature feature in layer.Features
            .Where(f => f.GeometryType == VectorGeometryType.LineString && GetNameRank(f.GetString("class"), zoom) >= 0)
            .OrderByDescending(f => GetNameRank(f.GetString("class"), zoom)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? name = feature.GetString("name:ja") ?? feature.GetString("name");
            if (string.IsNullOrWhiteSpace(name)) { continue; }
            PathLabelRenderer.Render(canvas, feature, tileToCanvasMatrix, name, font,
                fillPaint, haloPaint, collisionGrid, viewport, "road:" + name);
        }
    }

    private static int GetNameRank(string? roadClass, int zoom) => roadClass switch
    {
        "motorway" or "trunk" => zoom >= 12 ? 5 : -1,
        "primary" => zoom >= 12 ? 4 : -1,
        "secondary" => zoom >= 13 ? 3 : -1,
        "tertiary" => zoom >= 14 ? 2 : -1,
        "minor" or "residential" or "unclassified" or "service" => zoom >= 15 ? 1 : -1,
        "aerialway" => zoom >= 14 ? 1 : -1,
        "track" or "path" or "pedestrian" or "footway" or "cycleway" or "steps" or "living_street" => zoom >= 17 ? 0 : -1,
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
            _ => (SKColors.White, new SKColor(0x00, 0x62, 0xB8), new SKColor(0x00, 0x62, 0xB8)),
        };

        using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var outlinePaint = new SKPaint
        {
            Color = outline,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = kind == RouteShieldKind.Prefectural ? 1.8f : 1.2f,
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
