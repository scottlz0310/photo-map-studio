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
        // 森の樹木の模様は、ズーム 14 以上で、植生（landcover）の森にだけ重ねる
        bool trees = zoom >= OpenMapTilesTreeTexture.MinZoom;
        RenderLandcover(canvas, tile.GetLayer("landcover"), landuse: false, view, trees, tileScale);
        RenderLandcover(canvas, tile.GetLayer("landuse"), landuse: true, view, false, tileScale);
        RenderLayerPolygons(canvas, tile.GetLayer("park"), this.parkPaint, view, include: feature => OpenMapTilesLandPalette.ShouldFillPark(feature.GetString("class")));

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

        RenderWaterways(canvas, tile.GetLayer("waterway"), this.railwayPaint, this.waterPaint.Color, tileScale, zoom, view);
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
            // 林道は舗装路の下に描く（交差部で舗装路の中塗りが残る）。道路の後に鉄道の枕木を重ねる。
            RenderTracks(canvas, roadLayer, railway, tileScale, zoom, view);
            RenderRoads(canvas, roadLayer, tileScale, zoom, view);
            using SKPaint path = railway.Clone();
            path.Color = new SKColor(0xB9, 0x9C, 0x80, 160);
            path.StrokeWidth = 1.2f * Math.Max(1f, ZoomScale(zoom) * 0.45f) / tileScale;
            float pathDashOn = 4f / tileScale;
            float pathDashOff = 3f / tileScale;
            using SKPathEffect pathDash = SKPathEffect.CreateDash([pathDashOn, pathDashOff], 0);
            path.PathEffect = pathDash;
            using SKPaint cyclePath = path.Clone();
            cyclePath.Color = CyclePathColor;
            using SKPaint footPath = path.Clone();
            footPath.Color = FootPathColor;
            RenderDashedPaths(canvas, roadLayer, path, pathDashOn, pathDashOff, view, feature => IsPath(feature.GetString("class")), feature => GetPathKind(feature) switch
            {
                PathKind.Cycle => cyclePath,
                PathKind.Foot => footPath,
                _ => path,
            });
            if (zoom >= AerialwayMinZoom)
            {
                RenderAerialways(canvas, roadLayer, tileScale, view);
            }

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
            this.roadNameFont, this.roadNameFillPaint, this.roadNameHaloPaint, minor: false, cancellationToken);
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

        OpenMapTilesPeakSymbols.Render(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport, this.placeTypeface, this.textHaloPaint, cancellationToken);

        VectorTileLayer? placeLayer = tile.GetLayer("place");
        if (placeLayer is not null)
        {
            using var cityFont = new SKFont(this.placeTypeface, 16f);
            cityFont.Embolden = true;
            using var townFont = new SKFont(this.placeTypeface, 14f);
            townFont.Embolden = true;
            using var villageFont = new SKFont(this.placeTypeface, 12.5f);
            using var otherFont = new SKFont(this.placeTypeface, 11.5f);
            foreach (VectorTileFeature feature in placeLayer.Features.OrderBy(f => f.GetInt64("rank") ?? long.MaxValue))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SKFont font = feature.GetString("class") switch
                {
                    "city" => cityFont,
                    "town" => townFont,
                    "village" => villageFont,
                    _ => otherFont,
                };
                this.RenderPointLabel(canvas, feature, tileToCanvasMatrix, font, collisionGrid, viewport, zoom, poi: false);
            }
        }

        VectorTileLayer? poiLayer = tile.GetLayer("poi");
        if (zoom >= 15 && poiLayer is not null)
        {
            using var font = new SKFont(this.placeTypeface, 11f);
            foreach (VectorTileFeature feature in poiLayer.Features.OrderBy(f => f.GetInt64("rank") ?? long.MaxValue))
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.RenderPointLabel(canvas, feature, tileToCanvasMatrix, font, collisionGrid, viewport, zoom, poi: true);
            }
        }
    }

    /// <inheritdoc />
    public void RenderLowPriorityLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(collisionGrid);

        // 小道・林道の名前と番地は最も優先度が低く、全タイルの通常の注記を置いた後に、重ならない所だけに描く
        OpenMapTilesRoadSymbols.RenderNames(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport,
            this.roadNameFont, this.roadNameFillPaint, this.roadNameHaloPaint, minor: true, cancellationToken);
        OpenMapTilesHouseNumbers.Render(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport, this.placeTypeface, cancellationToken);
    }

    private void RenderPointLabel(SKCanvas canvas, VectorTileFeature feature, SKMatrix matrix, SKFont font,
        LabelCollisionGrid grid, SKRect viewport, int zoom, bool poi)
    {
        if (feature.GeometryType != VectorGeometryType.Point || feature.GeometryParts.Count == 0 || feature.GeometryParts[0].Count == 0) { return; }
        string? name = feature.GetString("name:ja") ?? feature.GetString("name");
        string? poiClass = poi ? feature.GetString("class") : null;
        PoiCategory category = OpenMapTilesPoiSymbols.GetCategory(poiClass);
        PoiMarker marker = OpenMapTilesPoiSymbols.GetMarker(poiClass);
        if (poi && zoom < OpenMapTilesPoiSymbols.GetMinZoom(marker)) { return; }
        bool nameless = string.IsNullOrWhiteSpace(name);
        if (nameless && !(poi && category == PoiCategory.Parking && zoom >= OpenMapTilesPoiSymbols.NamelessParkingMinZoom)) { return; }
        VectorPoint point = feature.GeometryParts[0][0];
        SKPoint anchor = matrix.MapPoint(new SKPoint(point.X, point.Y));
        if (nameless)
        {
            // 名前のない駐車場は記号だけを描く（注記と同じ衝突判定を通す）
            float half = OpenMapTilesPoiSymbols.MarkerHalfSize;
            if (grid.TryPlace([new SKRect(anchor.X - half, anchor.Y - half, anchor.X + half, anchor.Y + half)], viewport))
            {
                OpenMapTilesPoiSymbols.DrawMarker(canvas, anchor, marker, category, this.textHaloPaint, this.shieldFont);
            }

            return;
        }

        float width = font.MeasureText(name);
        float baseline = anchor.Y + (poi ? 15f : font.Size * 0.35f);
        var bounds = new SKRect(anchor.X - (width / 2f) - 2f, baseline - font.Size - 2f, anchor.X + (width / 2f) + 2f, baseline + 4f);
        if (poi) { bounds.Top = Math.Min(bounds.Top, anchor.Y - OpenMapTilesPoiSymbols.MarkerHalfSize - 1f); }
        if (!grid.TryPlace([bounds], viewport, (poi ? "poi:" : "place:") + name, poi ? 140f : 220f)) { return; }
        SKPaint textPaint = this.placeFillPaint;
        using SKPaint? categoryText = poi ? this.placeFillPaint.Clone() : null;
        if (poi)
        {
            OpenMapTilesPoiSymbols.DrawMarker(canvas, anchor, marker, category, this.textHaloPaint, this.shieldFont);
            categoryText!.Color = OpenMapTilesPoiSymbols.GetTextColor(category);
            textPaint = categoryText;
        }
        canvas.DrawText(name, anchor.X, baseline, SKTextAlign.Center, font, this.textHaloPaint);
        canvas.DrawText(name, anchor.X, baseline, SKTextAlign.Center, font, textPaint);
    }

    private static void RenderLandcover(SKCanvas canvas, VectorTileLayer? layer, bool landuse, in TileView view, bool trees, float tileScale)
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

            if (OpenMapTilesLandPalette.GetFill(landuse, feature.GetString("class")) is not { } color) { continue; }
            paint.Color = color;
            if (trees && feature.GetString("class") == "wood")
            {
                using SKPath outline = BuildPolygonPath(feature);
                canvas.DrawPath(outline, paint);
                OpenMapTilesTreeTexture.Draw(canvas, outline, tileScale);
                continue;
            }

            RenderPolygon(canvas, feature, paint);
        }
    }

    private static void RenderLayerPolygons(
        SKCanvas canvas,
        VectorTileLayer? layer,
        SKPaint fillPaint,
        in TileView view,
        SKPaint? strokePaint = null,
        Func<VectorTileFeature, bool>? include = null)
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
            if (feature.GeometryType != VectorGeometryType.Polygon || !view.IsVisible(bounds, i, view.Margin) || include?.Invoke(feature) == false)
            {
                continue;
            }

            RenderPolygon(canvas, feature, fillPaint, strokePaint);
        }
    }

    private static void RenderPolygon(SKCanvas canvas, VectorTileFeature feature, SKPaint fillPaint, SKPaint? strokePaint = null)
    {
        using SKPath path = BuildPolygonPath(feature);
        canvas.DrawPath(path, fillPaint);
        if (strokePaint is not null)
        {
            canvas.DrawPath(path, strokePaint);
        }
    }

    private static SKPath BuildPolygonPath(VectorTileFeature feature)
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

        return builder.Detach();
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
    internal static void RenderDashedPaths(SKCanvas canvas, VectorTileLayer layer, SKPaint paint, float dashOn, float dashOff, in TileView view, Func<VectorTileFeature, bool> include, Func<VectorTileFeature, SKPaint>? paintFor = null)
    {
        LayerBounds bounds = LayerBounds.For(layer);
        float margin = view.StrokeMargin(paint.StrokeWidth);
        var area = new SKRect(view.Clip.Left - margin, view.Clip.Top - margin, view.Clip.Right + margin, view.Clip.Bottom + margin);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString || !include(feature) || !view.IsVisible(bounds, i, margin))
            {
                continue;
            }

            SKRect extent = bounds.Of(i);
            SKPaint featurePaint = paintFor?.Invoke(feature) ?? paint;
            if (extent.Left >= area.Left && extent.Right <= area.Right && extent.Top >= area.Top && extent.Bottom <= area.Bottom)
            {
                DrawFeatureLines(canvas, feature, featurePaint);
                continue;
            }

            DrawClippedDashedLines(canvas, feature, featurePaint, dashOn, dashOff, area);
        }
    }

    /// <summary>林道を、小道より少し太く長い茶色の破線で描く。舗装路（casing つきの実線）とは区別できる。</summary>
    private static void RenderTracks(SKCanvas canvas, VectorTileLayer layer, SKPaint basePaint, float tileScale, int zoom, in TileView view)
    {
        using SKPaint track = basePaint.Clone();
        track.Color = TrackColor;
        track.StrokeWidth = 1.6f * Math.Max(1f, ZoomScale(zoom) * 0.45f) / tileScale;
        float dashOn = TrackDashOn / tileScale;
        float dashOff = TrackDashOff / tileScale;
        using SKPathEffect dash = SKPathEffect.CreateDash([dashOn, dashOff], 0);
        track.PathEffect = dash;
        RenderDashedPaths(canvas, layer, track, dashOn, dashOff, view, feature => IsTrack(feature.GetString("class")));
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
            if (IsPath(roadClass) || IsTrack(roadClass)) { continue; }
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

    internal static (int order, SKColor casingColor, SKColor fillColor, float casingWidth, float fillWidth) GetRoadStyle(string roadClass, int zoom)
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
            "service" => (30, 3.5f, "#ffffff", "#d5cfc5"),
            _ => (0, 0f, "#00000000", "#00000000"),
        };
        return (order, SKColor.Parse(casing), SKColor.Parse(fill), width > 0 ? (width * scale) + 2f : 0f, width * scale);
    }

    /// <summary>ロープウェイ・ゴンドラなどの索道を描く最小のズーム。</summary>
    private const int AerialwayMinZoom = 12;

    private static readonly SKColor CyclePathColor = new(0x4F, 0x86, 0xD6, 190);
    private static readonly SKColor FootPathColor = new(0xC9, 0x7F, 0x6E, 170);

    /// <summary>林道（<c>class=track</c>）の線の色。小道より濃い茶色で、舗装路の白い道とは別物に見える。</summary>
    private static readonly SKColor TrackColor = new(0x9A, 0x6B, 0x3A, 230);

    /// <summary>林道の破線の、線と間の長さ（論理ピクセル）。小道（4 と 3）より線が長い。</summary>
    private const float TrackDashOn = 6f;

    private const float TrackDashOff = 3f;

    /// <summary>小道の種類。色を分ける。</summary>
    internal enum PathKind
    {
        /// <summary>踏み分け道・山道など（茶色）。</summary>
        Trail,

        /// <summary>自転車道（青）。</summary>
        Cycle,

        /// <summary>歩道・歩行者専用道・階段（ピンクがかった茶色）。</summary>
        Foot,
    }

    /// <summary>
    /// 小道の種類を決める。OpenMapTiles では、<c>class</c> が <c>path</c> で、<c>subclass</c> が細目（<c>footway</c>・<c>cycleway</c> など）。
    /// </summary>
    internal static PathKind GetPathKind(VectorTileFeature feature)
        => (feature.GetString("subclass") ?? feature.GetString("class")) switch
        {
            "cycleway" => PathKind.Cycle,
            "footway" or "pedestrian" or "steps" or "corridor" or "platform" => PathKind.Foot,
            _ => PathKind.Trail,
        };

    /// <summary>索道（<c>transportation</c> の <c>class=aerialway</c>）を、灰色の線と等間隔の点で描く。</summary>
    private static void RenderAerialways(SKCanvas canvas, VectorTileLayer layer, float tileScale, in TileView view)
    {
        using var line = new SKPaint { Color = new SKColor(0x55, 0x55, 0x55), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f / tileScale, IsAntialias = true };
        using var ticks = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), Style = SKPaintStyle.Stroke, StrokeWidth = 3.6f / tileScale, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        using SKPathEffect tickDash = SKPathEffect.CreateDash([0.01f / tileScale, 8f / tileScale], 0);
        ticks.PathEffect = tickDash;
        LayerBounds bounds = LayerBounds.For(layer);
        float margin = view.StrokeMargin(ticks.StrokeWidth);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            view.ThrowIfCancellationRequested();
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString || feature.GetString("class") != "aerialway" || !view.IsVisible(bounds, i, margin))
            {
                continue;
            }

            DrawFeatureLines(canvas, feature, line);
            DrawFeatureLines(canvas, feature, ticks);
        }
    }

    internal static bool IsPath(string? roadClass) => roadClass is "path" or "pedestrian" or "footway" or "cycleway" or "steps";

    internal static bool IsTrack(string? roadClass) => roadClass == "track";

    /// <summary>川・運河の線幅（論理ピクセル。<see cref="ZoomScale"/> を掛ける）。</summary>
    private const float RiverWidth = 4f;

    /// <summary>運河の線幅。川より少し細い。</summary>
    private const float CanalWidth = 3f;

    /// <summary>沢・溝・排水路など、細い水路の線幅。</summary>
    private const float StreamWidth = 1.8f;

    internal static float GetWaterwayWidth(string? waterwayClass) => waterwayClass switch
    {
        "river" => RiverWidth,
        "canal" => CanalWidth,
        _ => StreamWidth,
    };

    /// <summary>水路（<c>waterway</c>）を、<c>class</c> ごとの太さで描く。線幅は <see cref="ZoomScale"/> と同じ伸び方。</summary>
    private static void RenderWaterways(SKCanvas canvas, VectorTileLayer? layer, SKPaint basePaint, SKColor color, float tileScale, int zoom, in TileView view)
    {
        if (layer is null) { return; }

        // 共有のペイントは書き換えず、太さごとに複製を使う（並行して描画しても線幅が混ざらない）
        var paints = new Dictionary<float, SKPaint>();
        try
        {
            LayerBounds bounds = LayerBounds.For(layer);
            for (int i = 0; i < layer.Features.Count; i++)
            {
                view.ThrowIfCancellationRequested();
                VectorTileFeature feature = layer.Features[i];
                if (feature.GeometryType != VectorGeometryType.LineString)
                {
                    continue;
                }

                float width = GetWaterwayWidth(feature.GetString("class"));
                if (!paints.TryGetValue(width, out SKPaint? paint))
                {
                    paint = basePaint.Clone();
                    paint.Color = color;
                    paint.StrokeWidth = width * ZoomScale(zoom) / tileScale;
                    paints[width] = paint;
                }

                if (view.IsVisible(bounds, i, view.StrokeMargin(paint.StrokeWidth)))
                {
                    DrawFeatureLines(canvas, feature, paint);
                }
            }
        }
        finally
        {
            foreach (SKPaint paint in paints.Values)
            {
                paint.Dispose();
            }
        }
    }

    private static float ZoomScale(int zoom) => (float)Math.Pow(2, (zoom - 16) * 0.75);

    internal static void DrawFeatureLines(SKCanvas canvas, VectorTileFeature feature, SKPaint paint)
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
