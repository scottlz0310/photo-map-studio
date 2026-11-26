using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// 航空写真などの上へ重ねる、OpenMapTiles / OpenFreeMap スキーマの道路だけのスタイル。
/// </summary>
/// <remarks>
/// 背景は透明で、面・地名・施設・鉄道・水路は描かない。道路の芯線（暗い縁つき）・道路名（白フチ）・経路番号の盾を描く。
/// トンネルの道路は破線にする。歩道・階段・通路・ホームと、トンネルの林道・小道は、写真に写らないものや道路に沿う線で
/// 写真を覆うため描かない。
/// 線は不透明にする。半透明にすると、隣り合うタイルが重ねて持つ部分（バッファ）が二重に合成され、タイルの境界が濃く見える。
/// 道路の色と重なりの順は <see cref="OpenMapTilesStyle"/> と同じで、名前と盾の配置も共有する。
/// </remarks>
public sealed class OpenMapTilesRoadOverlayStyle : IVectorMapStyle, IDisposable
{
    /// <summary>林道（<c>class=track</c>）を描く最小のズーム。</summary>
    private const int TrackMinZoom = 13;

    /// <summary>小道（<c>path</c>・<c>footway</c> など）を描く最小のズーム。市街地の歩道が写真を覆わないようにする。</summary>
    private const int PathMinZoom = 15;

    /// <summary>縁が芯線の両側へはみ出す幅の合計（論理ピクセル）。</summary>
    private const float CasingExtraWidth = 2f;

    private const float TrackWidth = 1.6f;
    private const float PathWidth = 1.2f;
    private const float DashOn = 6f;
    private const float DashOff = 3f;

    private static readonly SKColor CasingColor = new(0x26, 0x26, 0x26);
    private static readonly SKColor TrackColor = new(0xFF, 0xA9, 0x4D);
    private static readonly SKColor PathColor = new(0xFF, 0xE3, 0xC2);

    private readonly SKTypeface typeface = VectorMapFontHelper.CreateJapaneseTypeface();
    private readonly SKFont roadNameFont;
    private readonly SKFont shieldFont;

    private readonly SKPaint roadNameFillPaint = new()
    {
        Color = new SKColor(0x1A, 0x1A, 0x1A),
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    // 写真の上でも読めるように、標準の地図より白フチを太くする
    private readonly SKPaint roadNameHaloPaint = new()
    {
        Color = SKColors.White,
        Style = SKPaintStyle.StrokeAndFill,
        StrokeWidth = 3.5f,
        StrokeJoin = SKStrokeJoin.Round,
        IsAntialias = true,
    };

    /// <summary>
    /// 道路だけの重ね表示のスタイルを初期化する。
    /// </summary>
    public OpenMapTilesRoadOverlayStyle()
    {
        this.roadNameFont = new SKFont(this.typeface, 12f);
        this.shieldFont = new SKFont(this.typeface, 9f) { Embolden = true };
    }

    /// <inheritdoc />
    public string StyleId => "openmaptiles-road-overlay";

    /// <inheritdoc />
    public SKColor BackgroundColor => SKColors.Transparent;

    /// <summary>面は描かない（下の写真を見せる）。</summary>
    /// <inheritdoc />
    public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        cancellationToken.ThrowIfCancellationRequested();

        VectorTileLayer? roadLayer = tile.GetLayer("transportation");
        if (roadLayer is null)
        {
            return;
        }

        var view = new TileView(canvas.LocalClipBounds, 2f / tileScale, cancellationToken);

        // 破線の林道・小道は舗装路の下に描く
        if (zoom >= TrackMinZoom)
        {
            RenderDashedRoads(canvas, roadLayer, TrackColor, TrackWidth, tileScale, view,
                static feature => OpenMapTilesStyle.IsTrack(feature.GetString("class")) && !IsTunnel(feature));
        }

        if (zoom >= PathMinZoom)
        {
            RenderDashedRoads(canvas, roadLayer, PathColor, PathWidth, tileScale, view,
                static feature => IsOverlayPath(feature) && !IsTunnel(feature));
        }

        RenderPavedRoads(canvas, roadLayer, tileScale, zoom, view);
    }

    /// <inheritdoc />
    public void RenderSymbols(
        SKCanvas canvas,
        VectorTile tile,
        SKMatrix tileToCanvasMatrix,
        int zoom,
        LabelCollisionGrid collisionGrid,
        SKRect viewport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(collisionGrid);
        cancellationToken.ThrowIfCancellationRequested();
        OpenMapTilesRoadSymbols.RenderShields(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport, this.shieldFont, cancellationToken);
    }

    /// <inheritdoc />
    public void RenderLineLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        => this.RenderRoadNames(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport, minor: false, cancellationToken);

    /// <summary>地名・施設名などの点の注記は描かない。</summary>
    /// <inheritdoc />
    public void RenderLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(collisionGrid);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public void RenderLowPriorityLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        => this.RenderRoadNames(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport, minor: true, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        this.roadNameFont.Dispose();
        this.shieldFont.Dispose();
        this.roadNameFillPaint.Dispose();
        this.roadNameHaloPaint.Dispose();
        this.typeface.Dispose();
    }

    /// <summary>芯線の太さ（論理ピクセル。ズーム 16 のとき）。描かない種別は 0。</summary>
    private static float GetCenterLineWidth(string roadClass) => roadClass switch
    {
        "motorway" => 3.5f,
        "trunk" => 3.2f,
        "primary" => 3f,
        "secondary" => 2.7f,
        "tertiary" => 2.4f,
        "minor" or "residential" or "unclassified" or "living_street" => 1.8f,
        "service" => 1.2f,
        _ => 0f,
    };

    // 芯線なので、ズームによる太さの変化は小さくする
    private static float ZoomWidthScale(int zoom) => Math.Clamp((float)Math.Pow(2, (zoom - 16) * 0.25), 0.7f, 1.5f);

    private static bool IsTunnel(VectorTileFeature feature) => feature.GetString("brunnel") == "tunnel";

    /// <summary>重ねる小道。歩道・階段・通路・ホームは含めない（名前の注記にも同じ条件を使う）。</summary>
    private static bool IsOverlayPath(VectorTileFeature feature)
        => OpenMapTilesStyle.IsPath(feature.GetString("class")) && !IsExcludedFootway(feature);

    private static bool IsExcludedFootway(VectorTileFeature feature)
        => (feature.GetString("subclass") ?? feature.GetString("class")) is "footway" or "steps" or "corridor" or "platform";

    private static void RenderDashedRoads(SKCanvas canvas, VectorTileLayer layer, SKColor color, float width, float tileScale,
        in TileView view, Func<VectorTileFeature, bool> include)
    {
        float dashOn = DashOn / tileScale;
        float dashOff = DashOff / tileScale;
        using SKPathEffect dash = SKPathEffect.CreateDash([dashOn, dashOff], 0);
        using var casing = new SKPaint
        {
            Color = CasingColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (width + CasingExtraWidth) / tileScale,
            PathEffect = dash,
            IsAntialias = true,
        };
        OpenMapTilesStyle.RenderDashedPaths(canvas, layer, casing, dashOn, dashOff, view, include);
        using SKPaint line = casing.Clone();
        line.Color = color;
        line.StrokeWidth = width / tileScale;
        OpenMapTilesStyle.RenderDashedPaths(canvas, layer, line, dashOn, dashOff, view, include);
    }

    private static void RenderPavedRoads(SKCanvas canvas, VectorTileLayer layer, float tileScale, int zoom, in TileView view)
    {
        var roads = new List<(VectorTileFeature Feature, int Order, SKColor Color, float Width)>();
        LayerBounds bounds = LayerBounds.For(layer);
        float zoomScale = ZoomWidthScale(zoom);

        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            string roadClass = feature.GetString("class") ?? string.Empty;
            float width = GetCenterLineWidth(roadClass) * zoomScale;
            if (width > 0f && view.IsVisible(bounds, i, view.StrokeMargin((width + CasingExtraWidth) / tileScale)))
            {
                var style = OpenMapTilesStyle.GetRoadStyle(roadClass, zoom);
                roads.Add((feature, style.order, style.fillColor, width));
            }
        }

        roads.Sort((a, b) => a.Order.CompareTo(b.Order));

        using SKPathEffect tunnelDash = SKPathEffect.CreateDash([DashOn / tileScale, DashOff / tileScale], 0);

        // 全ての縁を描いてから全ての芯線を描き、交差点で縁が上の道路を分断しないようにする
        foreach (var road in roads)
        {
            view.ThrowIfCancellationRequested();
            DrawRoad(canvas, road.Feature, CasingColor, (road.Width + CasingExtraWidth) / tileScale, tunnelDash);
        }

        foreach (var road in roads)
        {
            view.ThrowIfCancellationRequested();
            DrawRoad(canvas, road.Feature, road.Color, road.Width / tileScale, tunnelDash);
        }
    }

    private static void DrawRoad(SKCanvas canvas, VectorTileFeature feature, SKColor color, float strokeWidth, SKPathEffect tunnelDash)
    {
        bool tunnel = IsTunnel(feature);
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            // 破線の端を丸くすると間隔が埋まるため、トンネルは端を伸ばさない
            StrokeCap = tunnel ? SKStrokeCap.Butt : SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            PathEffect = tunnel ? tunnelDash : null,
            IsAntialias = true,
        };
        OpenMapTilesStyle.DrawFeatureLines(canvas, feature, paint);
    }

    private void RenderRoadNames(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport, bool minor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(collisionGrid);
        cancellationToken.ThrowIfCancellationRequested();

        // 線を描かない索道・歩道などは、名前も出さない
        OpenMapTilesRoadSymbols.RenderNames(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport,
            this.roadNameFont, this.roadNameFillPaint, this.roadNameHaloPaint, minor, cancellationToken,
            include: static feature => feature.GetString("class") != "aerialway" && !IsExcludedFootway(feature));
    }
}
