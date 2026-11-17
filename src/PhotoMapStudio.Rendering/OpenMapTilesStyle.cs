using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// OpenMapTiles / OpenFreeMap スキーマに対応した標準ベクター地図スタイル。
/// </summary>
public sealed class OpenMapTilesStyle : IVectorMapStyle, IDisposable
{
    /// <inheritdoc />
    public string StyleId => "openmaptiles";

    /// <inheritdoc />
    public SKColor BackgroundColor => new(0xEB, 0xE8, 0xE1);

    // 面（ポリゴン）用ペイント
    private readonly SKPaint waterPaint = new()
    {
        Color = new SKColor(0xA0, 0xC8, 0xF0),
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint parkPaint = new()
    {
        Color = new SKColor(0xD8, 0xE8, 0xC8),
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint buildingFillPaint = new()
    {
        Color = new SKColor(0xDC, 0xD5, 0xC9),
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint buildingStrokePaint = new()
    {
        Color = new SKColor(0xC9, 0xC0, 0xB1),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 0.5f,
        IsAntialias = true,
    };

    // 鉄道ペイント
    private readonly SKPaint railwayPaint = new()
    {
        Color = new SKColor(0x77, 0x77, 0x77),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.2f,
        IsAntialias = true,
    };

    // テキスト用ペイント・フォント
    private readonly SKTypeface placeTypeface = VectorMapFontHelper.CreateJapaneseTypeface();
    private readonly SKFont roadNameFont;
    private readonly SKFont shieldFont;
    private readonly bool cullToClip;

    /// <summary>
    /// OpenMapTiles ベクタースタイルを初期化する。
    /// </summary>
    public OpenMapTilesStyle()
        : this(cullToClip: true)
    {
    }

    /// <param name="cullToClip">
    /// 描画先の範囲と交わらない地物を描かない（既定）。<see langword="false"/> は、全ての地物を全長で描く基準の経路で、
    /// 最適化が出力を変えないことをテストで比べるために使う。
    /// </param>
    internal OpenMapTilesStyle(bool cullToClip)
    {
        this.cullToClip = cullToClip;
        this.roadNameFont = new SKFont(this.placeTypeface, 11f);
        this.shieldFont = new SKFont(this.placeTypeface, 9f) { Embolden = true };
    }
    private readonly SKPaint placeFillPaint = new()
    {
        Color = new SKColor(0x22, 0x22, 0x22),
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint textHaloPaint = new()
    {
        Color = SKColors.White,
        Style = SKPaintStyle.StrokeAndFill,
        StrokeWidth = 3f,
        IsAntialias = true,
    };

    private readonly SKPaint roadNameFillPaint = new()
    {
        Color = new SKColor(0x44, 0x44, 0x44),
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint roadNameHaloPaint = new()
    {
        Color = SKColors.White,
        Style = SKPaintStyle.StrokeAndFill,
        StrokeWidth = 2.5f,
        IsAntialias = true,
    };

    private TileView CreateView(SKCanvas canvas, float tileScale, CancellationToken cancellationToken)
        => new(this.cullToClip ? canvas.LocalClipBounds : SKRect.Create(float.MinValue / 4f, float.MinValue / 4f, float.MaxValue / 2f, float.MaxValue / 2f), 2f / tileScale, cancellationToken);

    /// <inheritdoc />
    public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);

        // 描画先の範囲（タイル座標）と交わらない地物は描かない（出力は変わらない）。縁の半透明画素の分として 2px を足す
        var view = this.CreateView(canvas, tileScale, cancellationToken);

        // 1. 緑地・土地利用（landcover / landuse / park）
        RenderLandcover(canvas, tile.GetLayer("landcover"), landuse: false, view);
        RenderLandcover(canvas, tile.GetLayer("landuse"), landuse: true, view);
        RenderLayerPolygons(canvas, tile.GetLayer("park"), this.parkPaint, view);

        // 2. 水域（water）
        RenderLayerPolygons(canvas, tile.GetLayer("water"), this.waterPaint, view);

        // 3. 建物（building）: ズーム 15 以上で描画
        if (zoom >= 15)
        {
            VectorTileLayer? buildingLayer = tile.GetLayer("building");
            if (buildingLayer is not null)
            {
                using SKPaint outline = this.buildingStrokePaint.Clone();
                outline.StrokeWidth = 1f / tileScale;
                RenderLayerPolygons(canvas, buildingLayer, this.buildingFillPaint, view, outline);
            }
        }
    }

    /// <inheritdoc />
    public void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);

        // 描画先の範囲（タイル座標）と交わらない地物は描かない（出力は変わらない）。縁の半透明画素の分として 2px を足す
        var view = this.CreateView(canvas, tileScale, cancellationToken);

        // 共有のペイントは書き換えず、描画ごとに線幅を設定した複製を使う（並行して描画しても線幅が混ざらない）
        using SKPaint railway = this.railwayPaint.Clone();
        railway.StrokeWidth = 2.5f / tileScale;

        using SKPaint river = this.railwayPaint.Clone();
        river.Color = this.waterPaint.Color;
        river.StrokeWidth = 2.5f * ZoomScale(zoom) / tileScale;
        RenderLayerLines(canvas, tile.GetLayer("waterway"), river, view);
        using SKPaint boundary = this.railwayPaint.Clone();
        boundary.Color = new SKColor(0xB0, 0x8F, 0xB5);
        boundary.StrokeWidth = 1.8f / tileScale;
        using SKPathEffect boundaryDash = SKPathEffect.CreateDash([7f / tileScale, 4f / tileScale], 0);
        boundary.PathEffect = boundaryDash;
        VectorTileLayer? boundaries = tile.GetLayer("boundary");
        if (boundaries is not null)
        {
            LayerBounds bounds = LayerBounds.For(boundaries);
            float margin = view.StrokeMargin(boundary.StrokeWidth);
            for (int i = 0; i < boundaries.Features.Count; i++)
            {
                view.ThrowIfCancellationRequested();
                VectorTileFeature feature = boundaries.Features[i];
                if (feature.GeometryType == VectorGeometryType.LineString && feature.GetInt64("admin_level") is <= 4
                    && view.IsVisible(bounds, i, margin))
                {
                    DrawFeatureLines(canvas, feature, boundary);
                }
            }
        }

        VectorTileLayer? roadLayer = tile.GetLayer("transportation");
        if (roadLayer is not null)
        {
            // 道路の後に鉄道の枕木を重ねる。
            RenderRoads(canvas, roadLayer, tileScale, zoom, view);
            using SKPaint path = railway.Clone();
            path.Color = new SKColor(0xB9, 0x9C, 0x80, 160);
            path.StrokeWidth = 1.2f * Math.Max(1f, ZoomScale(zoom) * 0.45f) / tileScale;
            float pathDashOn = 4f / tileScale;
            float pathDashOff = 3f / tileScale;
            using SKPathEffect pathDash = SKPathEffect.CreateDash([pathDashOn, pathDashOff], 0);
            path.PathEffect = pathDash;
            RenderDashedPaths(canvas, roadLayer, path, pathDashOn, pathDashOff, view);
            RenderTransportationRailways(canvas, roadLayer, railway, view);
            using SKPaint ties = railway.Clone();
            ties.Color = SKColors.White;
            ties.StrokeWidth = 1.2f / tileScale;
            using SKPathEffect railDash = SKPathEffect.CreateDash([6f / tileScale, 6f / tileScale], 0);
            ties.PathEffect = railDash;
            RenderTransportationRailways(canvas, roadLayer, ties, view);
        }

        // フォールバック: 別途 railway レイヤーが存在する場合も描画
        VectorTileLayer? fallbackRailwayLayer = tile.GetLayer("railway");
        if (fallbackRailwayLayer is not null)
        {
            RenderLayerLines(canvas, fallbackRailwayLayer, railway, view);
        }
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

        // 経路番号の盾（国道・都道府県道・高速）
        OpenMapTilesRoadSymbols.RenderShields(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport, this.shieldFont, cancellationToken);
    }

    /// <inheritdoc />
    public void RenderLineLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(collisionGrid);
        OpenMapTilesRoadSymbols.RenderNames(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport,
            this.roadNameFont, this.roadNameFillPaint, this.roadNameHaloPaint, cancellationToken);
    }

    /// <inheritdoc />
    public void RenderLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(collisionGrid);

        using SKPaint riverText = this.placeFillPaint.Clone();
        riverText.Color = new SKColor(0x33, 0x77, 0xAA);
        foreach (string layerName in new[] { "waterway", "water_name" })
        {
            VectorTileLayer? layer = tile.GetLayer(layerName);
            if (layer is null) { continue; }
            foreach (VectorTileFeature feature in layer.Features.Where(f => f.GeometryType == VectorGeometryType.LineString))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? name = feature.GetString("name:ja") ?? feature.GetString("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    PathLabelRenderer.Render(canvas, feature, tileToCanvasMatrix, name, this.roadNameFont,
                        riverText, this.roadNameHaloPaint, collisionGrid, viewport, "water:" + name);
                }
            }
        }

        VectorTileLayer? placeLayer = tile.GetLayer("place");
        if (placeLayer is not null)
        {
            foreach (VectorTileFeature feature in placeLayer.Features.OrderBy(f => f.GetInt64("rank") ?? long.MaxValue))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? kind = feature.GetString("class");
                float size = kind switch
                {
                    "city" => 16f,
                    "town" => 14f,
                    "village" => 12.5f,
                    _ => 11.5f,
                };
                using var font = new SKFont(this.placeTypeface, size) { Embolden = kind is "city" or "town" };
                this.RenderPointLabel(canvas, feature, tileToCanvasMatrix, font, collisionGrid, viewport, poi: false);
            }
        }

        VectorTileLayer? poiLayer = tile.GetLayer("poi");
        if (zoom >= 15 && poiLayer is not null)
        {
            using var font = new SKFont(this.placeTypeface, 11f);
            foreach (VectorTileFeature feature in poiLayer.Features.OrderBy(f => f.GetInt64("rank") ?? long.MaxValue))
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.RenderPointLabel(canvas, feature, tileToCanvasMatrix, font, collisionGrid, viewport, poi: true);
            }
        }
    }

    private void RenderPointLabel(SKCanvas canvas, VectorTileFeature feature, SKMatrix matrix, SKFont font,
        LabelCollisionGrid grid, SKRect viewport, bool poi)
    {
        if (feature.GeometryType != VectorGeometryType.Point || feature.GeometryParts.Count == 0 || feature.GeometryParts[0].Count == 0) { return; }
        string? name = feature.GetString("name:ja") ?? feature.GetString("name");
        if (string.IsNullOrWhiteSpace(name)) { return; }
        VectorPoint point = feature.GeometryParts[0][0];
        SKPoint anchor = matrix.MapPoint(new SKPoint(point.X, point.Y));
        float width = font.MeasureText(name);
        float baseline = anchor.Y + (poi ? 15f : font.Size * 0.35f);
        var bounds = new SKRect(anchor.X - (width / 2f) - 2f, baseline - font.Size - 2f, anchor.X + (width / 2f) + 2f, baseline + 4f);
        if (poi) { bounds.Top = Math.Min(bounds.Top, anchor.Y - 5f); }
        if (!grid.TryPlace([bounds], viewport, (poi ? "poi:" : "place:") + name, poi ? 140f : 220f)) { return; }
        if (poi)
        {
            using SKPaint dot = this.placeFillPaint.Clone();
            dot.Color = feature.GetString("class") switch
            {
                "hospital" => new SKColor(0xDA, 0x00, 0x00),
                "school" or "college" => new SKColor(0x39, 0x73, 0x9D),
                "station" or "railway" => new SKColor(0x79, 0x55, 0x48),
                _ => new SKColor(0xD5, 0x7B, 0x35),
            };
            canvas.DrawCircle(anchor, 4.5f, this.textHaloPaint);
            canvas.DrawCircle(anchor, 3.5f, dot);
        }
        canvas.DrawText(name, anchor.X, baseline, SKTextAlign.Center, font, this.textHaloPaint);
        canvas.DrawText(name, anchor.X, baseline, SKTextAlign.Center, font, this.placeFillPaint);
    }

    private static void RenderLandcover(SKCanvas canvas, VectorTileLayer? layer, bool landuse, in TileView view)
    {
        if (layer is null) { return; }
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        LayerBounds bounds = LayerBounds.For(layer);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.Polygon || !view.IsVisible(bounds, i, view.Margin))
            {
                continue;
            }

            string? color = (landuse, feature.GetString("class")) switch
            {
                (false, "wood") => "#c8dfb8",
                (false, "grass") => "#d8e8c8",
                (false, "wetland") => "#cfe2d6",
                (false, "sand") => "#f2e8c4",
                (false, "ice") => "#f4f8fb",
                (true, "pitch" or "track") => "#cfe6c4",
                (true, "cemetery") => "#d3dfcb",
                (true, "school" or "college" or "university" or "hospital") => "#ebe3cf",
                (true, "industrial" or "railway") => "#dfdad5",
                (true, "residential") => "#e6e1d8",
                _ => null,
            };
            if (color is null) { continue; }
            paint.Color = SKColor.Parse(color);
            RenderPolygon(canvas, feature, paint);
        }
    }

    private static void RenderLayerPolygons(
        SKCanvas canvas,
        VectorTileLayer? layer,
        SKPaint fillPaint,
        in TileView view,
        SKPaint? strokePaint = null)
    {
        if (layer is null)
        {
            return;
        }

        LayerBounds bounds = LayerBounds.For(layer);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.Polygon || !view.IsVisible(bounds, i, view.Margin))
            {
                continue;
            }

            RenderPolygon(canvas, feature, fillPaint, strokePaint);
        }
    }

    private static void RenderPolygon(SKCanvas canvas, VectorTileFeature feature, SKPaint fillPaint, SKPaint? strokePaint = null)
    {
        using var builder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
        for (int r = 0; r < feature.GeometryParts.Count; r++)
        {
            IReadOnlyList<VectorPoint> ring = feature.GeometryParts[r];
            if (ring.Count < 3)
            {
                continue;
            }

            builder.MoveTo(ring[0].X, ring[0].Y);
            for (int p = 1; p < ring.Count; p++)
            {
                builder.LineTo(ring[p].X, ring[p].Y);
            }
            builder.Close();
        }

        using SKPath path = builder.Detach();
        canvas.DrawPath(path, fillPaint);
        if (strokePaint is not null)
        {
            canvas.DrawPath(path, strokePaint);
        }
    }

    private static void RenderLayerLines(SKCanvas canvas, VectorTileLayer? layer, SKPaint paint, in TileView view)
    {
        if (layer is null) { return; }
        LayerBounds bounds = LayerBounds.For(layer);
        float margin = view.StrokeMargin(paint.StrokeWidth);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString || !view.IsVisible(bounds, i, margin))
            {
                continue;
            }

            using var builder = new SKPathBuilder();
            for (int p = 0; p < feature.GeometryParts.Count; p++)
            {
                IReadOnlyList<VectorPoint> line = feature.GeometryParts[p];
                if (line.Count < 2)
                {
                    continue;
                }

                builder.MoveTo(line[0].X, line[0].Y);
                for (int ptIdx = 1; ptIdx < line.Count; ptIdx++)
                {
                    builder.LineTo(line[ptIdx].X, line[ptIdx].Y);
                }
            }

            using SKPath path = builder.Detach();
            canvas.DrawPath(path, paint);
        }
    }

    private static void RenderTransportationRailways(SKCanvas canvas, VectorTileLayer layer, SKPaint paint, in TileView view)
    {
        LayerBounds bounds = LayerBounds.For(layer);
        float margin = view.StrokeMargin(paint.StrokeWidth);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString || !view.IsVisible(bounds, i, margin))
            {
                continue;
            }

            string cls = feature.GetString("class") ?? string.Empty;
            if (cls is "rail" or "transit" or "subway" or "tram")
            {
                DrawFeatureLines(canvas, feature, paint);
            }
        }
    }

    /// <summary>
    /// 破線の小道（path・footway など）を描く。表示範囲をはみ出す小道は、範囲（＋線幅）と交わる区間だけを、
    /// 元のパスの始点からの距離で破線の位相を合わせて描く（見た目は同じ）。
    /// Skia は破線をパスの全長に対して生成するため、深いズームで長い小道の大半が画面外になると、画面外の区間の処理が支配的になる。
    /// </summary>
    private static void RenderDashedPaths(SKCanvas canvas, VectorTileLayer layer, SKPaint paint, float dashOn, float dashOff, in TileView view)
    {
        LayerBounds bounds = LayerBounds.For(layer);
        float margin = view.StrokeMargin(paint.StrokeWidth);
        var area = new SKRect(view.Clip.Left - margin, view.Clip.Top - margin, view.Clip.Right + margin, view.Clip.Bottom + margin);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString || !IsPath(feature.GetString("class")) || !view.IsVisible(bounds, i, margin))
            {
                continue;
            }

            SKRect extent = bounds.Of(i);
            if (extent.Left >= area.Left && extent.Right <= area.Right && extent.Top >= area.Top && extent.Bottom <= area.Bottom)
            {
                DrawFeatureLines(canvas, feature, paint);
                continue;
            }

            DrawClippedDashedLines(canvas, feature, paint, dashOn, dashOff, area);
        }
    }

    private static void DrawClippedDashedLines(SKCanvas canvas, VectorTileFeature feature, SKPaint paint, float dashOn, float dashOff, SKRect area)
    {
        double period = dashOn + dashOff;
        for (int p = 0; p < feature.GeometryParts.Count; p++)
        {
            IReadOnlyList<VectorPoint> line = feature.GeometryParts[p];

            // 範囲と交わる連続した区間（頂点の添字の範囲）と、その始点までの距離を求める
            var runs = new List<(int First, int Last, double Distance)>();
            double distance = 0;
            int first = -1;
            double firstDistance = 0;
            for (int k = 0; k < line.Count - 1; k++)
            {
                VectorPoint a = line[k];
                VectorPoint b = line[k + 1];
                bool intersects = Math.Max(a.X, b.X) >= area.Left && Math.Min(a.X, b.X) <= area.Right
                    && Math.Max(a.Y, b.Y) >= area.Top && Math.Min(a.Y, b.Y) <= area.Bottom;
                if (intersects)
                {
                    if (first < 0)
                    {
                        first = k;
                        firstDistance = distance;
                    }
                }
                else if (first >= 0)
                {
                    runs.Add((first, k, firstDistance));
                    first = -1;
                }

                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                distance += Math.Sqrt((dx * dx) + (dy * dy));
            }

            if (first >= 0)
            {
                runs.Add((first, line.Count - 1, firstDistance));
            }

            foreach ((int runFirst, int runLast, double runDistance) in runs)
            {
                using var builder = new SKPathBuilder();
                builder.MoveTo(line[runFirst].X, line[runFirst].Y);
                for (int k = runFirst + 1; k <= runLast; k++)
                {
                    builder.LineTo(line[k].X, line[k].Y);
                }

                using SKPath path = builder.Detach();
                using SKPathEffect effect = SKPathEffect.CreateDash([dashOn, dashOff], (float)(runDistance % period));
                using SKPaint runPaint = paint.Clone();
                runPaint.PathEffect = effect;
                canvas.DrawPath(path, runPaint);
            }
        }
    }

    private static void RenderRoads(SKCanvas canvas, VectorTileLayer layer, float tileScale, int zoom, in TileView view)
    {
        var roadFeatures = new List<(VectorTileFeature feature, int order, SKColor casingColor, SKColor fillColor, float casingWidth, float fillWidth)>();
        LayerBounds bounds = LayerBounds.For(layer);

        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            string roadClass = feature.GetString("class") ?? string.Empty;
            if (IsPath(roadClass)) { continue; }
            var (order, casingColor, fillColor, casingWidth, fillWidth) = GetRoadStyle(roadClass, zoom);
            if (fillWidth > 0f && view.IsVisible(bounds, i, view.StrokeMargin(Math.Max(casingWidth, fillWidth) / tileScale)))
            {
                roadFeatures.Add((feature, order, casingColor, fillColor, casingWidth, fillWidth));
            }
        }

        // 描画順序（下位階層 -> 上位階層）でソート
        roadFeatures.Sort((a, b) => a.order.CompareTo(b.order));

        // パス 1: 外枠（casing）の描画
        for (int i = 0; i < roadFeatures.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            var road = roadFeatures[i];
            if (road.casingWidth > 0f)
            {
                using var paint = new SKPaint
                {
                    Color = road.casingColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = road.casingWidth / tileScale,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    IsAntialias = true,
                };
                DrawFeatureLines(canvas, road.feature, paint);
            }
        }

        // パス 2: 中塗り（fill）の描画
        for (int i = 0; i < roadFeatures.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            var road = roadFeatures[i];
            using var paint = new SKPaint
            {
                Color = road.fillColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = road.fillWidth / tileScale,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            DrawFeatureLines(canvas, road.feature, paint);
        }
    }

    private static (int order, SKColor casingColor, SKColor fillColor, float casingWidth, float fillWidth) GetRoadStyle(string roadClass, int zoom)
    {
        float scale = ZoomScale(zoom);
        var (order, width, fill, casing) = roadClass switch
        {
            "motorway" => (90, 9.5f, "#9ad8a8", "#4f9a62"),
            "trunk" => (80, 8.5f, "#ffb27a", "#d9803f"),
            "primary" => (70, 7.5f, "#ffe27a", "#cfa21f"),
            "secondary" => (60, 7f, "#ffe27a", "#cfa21f"),
            "tertiary" => (50, 6f, "#fff3c4", "#d8c27a"),
            "minor" or "residential" or "unclassified" or "living_street" => (40, 4.5f, "#ffffff", "#cfc9bf"),
            "service" or "track" => (30, 3.5f, "#ffffff", "#d5cfc5"),
            _ => (0, 0f, "#00000000", "#00000000"),
        };
        return (order, SKColor.Parse(casing), SKColor.Parse(fill), width > 0 ? (width * scale) + 2f : 0f, width * scale);
    }

    private static bool IsPath(string? roadClass) => roadClass is "path" or "pedestrian" or "footway" or "cycleway" or "steps";

    private static float ZoomScale(int zoom) => (float)Math.Pow(2, (zoom - 16) * 0.75);

    private static void DrawFeatureLines(SKCanvas canvas, VectorTileFeature feature, SKPaint paint)
    {
        using var builder = new SKPathBuilder();
        for (int p = 0; p < feature.GeometryParts.Count; p++)
        {
            IReadOnlyList<VectorPoint> line = feature.GeometryParts[p];
            if (line.Count < 2)
            {
                continue;
            }

            builder.MoveTo(line[0].X, line[0].Y);
            for (int ptIdx = 1; ptIdx < line.Count; ptIdx++)
            {
                builder.LineTo(line[ptIdx].X, line[ptIdx].Y);
            }
        }

        using SKPath path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.waterPaint.Dispose();
        this.parkPaint.Dispose();
        this.buildingFillPaint.Dispose();
        this.buildingStrokePaint.Dispose();
        this.railwayPaint.Dispose();
        this.roadNameFont.Dispose();
        this.shieldFont.Dispose();
        this.roadNameFillPaint.Dispose();
        this.roadNameHaloPaint.Dispose();
        this.placeTypeface.Dispose();
        this.placeFillPaint.Dispose();
        this.textHaloPaint.Dispose();
    }
}
