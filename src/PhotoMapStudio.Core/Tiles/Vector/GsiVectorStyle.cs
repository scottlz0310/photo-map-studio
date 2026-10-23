using System.Globalization;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 国土地理院の最適化ベクトルタイル（optimal_bvmap-v1）スキーマに対応した標準ベクター地図スタイル。
/// 地理院地形図・淡色地図調の配色（水域・等高線・道路階層・建物・注記）を再現します。
/// </summary>
public sealed class GsiVectorStyle : IVectorMapStyle, IDisposable
{
    /// <summary>
    /// 国土地理院の道路分類。
    /// </summary>
    public enum GsiRoadKind
    {
        /// <summary>高速自動車国道等。</summary>
        Highway,

        /// <summary>国道・主要道路。</summary>
        National,

        /// <summary>都道府県道。</summary>
        Prefectural,

        /// <summary>市区町村道・一般道路・その他。</summary>
        Minor,
    }

    /// <inheritdoc />
    public string StyleId => "gsi";

    /// <inheritdoc />
    public SKColor BackgroundColor => new(0xF8, 0xF7, 0xF2); // #F8F7F2 (地理院淡色調背景)

    // 面（ポリゴン）用ペイント
    private readonly SKPaint waterPaint = new()
    {
        Color = new SKColor(0xC4, 0xE5, 0xF8), // #C4E5F8
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint greenAreaPaint = new()
    {
        Color = new SKColor(0xE3, 0xEC, 0xD5), // #E3ECD5
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint buildingFillPaint = new()
    {
        Color = new SKColor(0xE6, 0xE2, 0xD8), // #E6E2D8
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint buildingStrokePaint = new()
    {
        Color = new SKColor(0xD4, 0xCF, 0xBF), // #D4CFBF
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 0.5f,
        IsAntialias = true,
    };

    // 線用ペイント
    private readonly SKPaint contourPaint = new()
    {
        Color = new SKColor(0xD4, 0xBA, 0x9E), // #D4BA9E (等高線)
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 0.8f,
        IsAntialias = true,
    };

    private readonly SKPaint riverPaint = new()
    {
        Color = new SKColor(0x70, 0xBB, 0xE8), // #70BBE8 (河川・水涯線)
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.0f,
        IsAntialias = true,
    };

    private readonly SKPaint railwayPaint = new()
    {
        Color = new SKColor(0x44, 0x44, 0x44), // #444444 (鉄道)
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.2f,
        IsAntialias = true,
    };

    private readonly SKPaint admBdryPaint = new()
    {
        Color = new SKColor(0x9E, 0x9E, 0x9E), // #9E9E9E (行政境界)
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.0f,
        IsAntialias = true,
        PathEffect = SKPathEffect.CreateDash([6f, 3f], 0),
    };

    // 道路ペイント (高速・国道・県道・一般)
    private readonly SKPaint highwayCasing = new() { Color = new SKColor(0x2E, 0x7D, 0x32), Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint highwayFill = new() { Color = new SKColor(0x4C, 0xAF, 0x50), Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint nationalCasing = new() { Color = new SKColor(0xBF, 0x36, 0x0C), Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint nationalFill = new() { Color = new SKColor(0xE6, 0x51, 0x00), Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint prefCasing = new() { Color = new SKColor(0xE6, 0x51, 0x00), Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint prefFill = new() { Color = new SKColor(0xF5, 0x7F, 0x17), Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint minorCasing = new() { Color = new SKColor(0xB0, 0xB0, 0xB0), Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint minorFill = new() { Color = SKColors.White, Style = SKPaintStyle.Stroke, IsAntialias = true };

    // 注記用ペイント・フォント
    private readonly SKTypeface labelTypeface = VectorMapFontHelper.CreateJapaneseTypeface();
    private readonly SKFont labelFont;

    /// <summary>
    /// 国土地理院 最適化ベクトルスタイルを初期化する。
    /// </summary>
    public GsiVectorStyle()
    {
        this.labelFont = new SKFont(this.labelTypeface, 11f);
    }
    private readonly SKPaint labelFillPaint = new()
    {
        Color = new SKColor(0x33, 0x33, 0x33), // #333333
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint labelHaloPaint = new()
    {
        Color = SKColors.White,
        Style = SKPaintStyle.StrokeAndFill,
        StrokeWidth = 2.5f,
        IsAntialias = true,
    };

    /// <inheritdoc />
    public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);

        // 1. 地形面・緑地 (TpgphArea)
        RenderLayerPolygons(canvas, tile.GetLayer("TpgphArea"), this.greenAreaPaint);

        // 2. 水域 (WA / WStrA)
        RenderLayerPolygons(canvas, tile.GetLayer("WA"), this.waterPaint);
        RenderLayerPolygons(canvas, tile.GetLayer("WStrA"), this.waterPaint);

        // 3. 建物 (BldA): ズーム 13 以上で描画
        if (zoom >= 13)
        {
            VectorTileLayer? buildingLayer = tile.GetLayer("BldA");
            if (buildingLayer is not null)
            {
                RenderLayerPolygons(canvas, buildingLayer, this.buildingFillPaint, this.buildingStrokePaint);
            }
        }
    }

    /// <inheritdoc />
    public void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);

        // 共有のペイントは書き換えず、描画ごとに線幅を設定した複製を使う（並行して描画しても線幅が混ざらない）

        // 1. 等高線 (Cntr)
        using (SKPaint contour = StrokeOf(this.contourPaint, 0.8f / tileScale))
        {
            RenderLayerLines(canvas, tile.GetLayer("Cntr"), contour);
        }

        // 2. 水涯線・河川中心線・海岸線 (WL / RvrCL / Cstline)
        using (SKPaint river = StrokeOf(this.riverPaint, 1.0f / tileScale))
        {
            RenderLayerLines(canvas, tile.GetLayer("WL"), river);
            RenderLayerLines(canvas, tile.GetLayer("RvrCL"), river);
            RenderLayerLines(canvas, tile.GetLayer("Cstline"), river);
        }

        // 3. 行政境界線 (AdmBdry)
        using (SKPaint admBdry = StrokeOf(this.admBdryPaint, 1.0f / tileScale))
        {
            RenderLayerLines(canvas, tile.GetLayer("AdmBdry"), admBdry);
        }

        // 4. 鉄道 (RailCL)
        using (SKPaint railway = StrokeOf(this.railwayPaint, 1.2f / tileScale))
        {
            RenderLayerLines(canvas, tile.GetLayer("RailCL"), railway);
        }

        // 5. 道路 (RdCL: 道路中心線 / RdEdg: 道路縁)
        VectorTileLayer? roadLayer = tile.GetLayer("RdCL");
        if (roadLayer is not null)
        {
            this.RenderRoads(canvas, roadLayer, tileScale);
        }

        VectorTileLayer? roadEdgeLayer = tile.GetLayer("RdEdg");
        if (roadEdgeLayer is not null && zoom >= 15)
        {
            using SKPaint roadEdge = StrokeOf(this.minorCasing, 0.8f / tileScale);
            RenderLayerLines(canvas, roadEdgeLayer, roadEdge);
        }
    }

    /// <inheritdoc />
    public void RenderLineLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom,
        LabelCollisionGrid collisionGrid, SKRect viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(collisionGrid);
        VectorTileLayer? layer = tile.GetLayer("Anno");
        if (layer is null) { return; }
        foreach (VectorTileFeature feature in layer.Features.Where(f => f.GeometryType == VectorGeometryType.LineString))
        {
            string? text = feature.GetString("vt_text") ?? feature.GetString("knj") ?? feature.GetString("name") ?? feature.GetString("text");
            if (string.IsNullOrWhiteSpace(text)) { continue; }
            PathLabelRenderer.Render(canvas, feature, tileToCanvasMatrix, text, this.labelFont,
                this.labelFillPaint, this.labelHaloPaint, collisionGrid, viewport, "gsi-line:" + text);
        }
    }

    /// <inheritdoc />
    public void RenderLabels(
        SKCanvas canvas,
        VectorTile tile,
        SKMatrix tileToCanvasMatrix,
        int zoom,
        LabelCollisionGrid collisionGrid,
        SKRect viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(collisionGrid);

        // 注記 (Anno)
        VectorTileLayer? annoLayer = tile.GetLayer("Anno");
        if (annoLayer is null)
        {
            return;
        }

        for (int i = 0; i < annoLayer.Features.Count; i++)
        {
            VectorTileFeature feature = annoLayer.Features[i];
            if (feature.GeometryType == VectorGeometryType.LineString || feature.GeometryParts.Count == 0 || feature.GeometryParts[0].Count == 0)
            {
                continue;
            }

            // 国土地理院 最適化ベクトルタイル現行仕様: vt_text (公式 std.json 準拠)
            string? text = feature.GetString("vt_text")
                ?? feature.GetString("knj")
                ?? feature.GetString("name")
                ?? feature.GetString("text");

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // ポイントまたはライン先頭をアンカー点とする
            VectorPoint pt = feature.GeometryParts[0][0];
            SKPoint canvasPt = tileToCanvasMatrix.MapPoint(new SKPoint(pt.X, pt.Y));

            float textWidth = this.labelFont.MeasureText(text);
            var bounds = new SKRect(
                canvasPt.X - (textWidth / 2f),
                canvasPt.Y - 11f,
                canvasPt.X + (textWidth / 2f),
                canvasPt.Y + 3f);

            if (collisionGrid.TryPlace(bounds, viewport))
            {
                // 白ハロー（縁取り）
                canvas.DrawText(text, bounds.Left, canvasPt.Y, SKTextAlign.Left, this.labelFont, this.labelHaloPaint);
                // テキスト本文
                canvas.DrawText(text, bounds.Left, canvasPt.Y, SKTextAlign.Left, this.labelFont, this.labelFillPaint);
            }
        }
    }

    private void RenderRoads(SKCanvas canvas, VectorTileLayer roadLayer, float tileScale)
    {
        // 種別・線幅ごとの複製ペイントをこの描画の間だけ保持する（共有のペイントは書き換えない）
        var strokes = new Dictionary<(GsiRoadKind Kind, float Width, bool Casing), SKPaint>();
        try
        {
            // 道路は casing (外枠) を先にすべて描き、その上に fill (中塗り) を描く
            for (int i = 0; i < roadLayer.Features.Count; i++)
            {
                VectorTileFeature feature = roadLayer.Features[i];
                if (feature.GeometryType != VectorGeometryType.LineString)
                {
                    continue;
                }

                var (kind, width) = GetRoadCategory(feature);
                RenderFeatureLine(canvas, feature, this.GetRoadStroke(strokes, kind, width + 1.6f, tileScale, casing: true));
            }

            for (int i = 0; i < roadLayer.Features.Count; i++)
            {
                VectorTileFeature feature = roadLayer.Features[i];
                if (feature.GeometryType != VectorGeometryType.LineString)
                {
                    continue;
                }

                var (kind, width) = GetRoadCategory(feature);
                RenderFeatureLine(canvas, feature, this.GetRoadStroke(strokes, kind, width, tileScale, casing: false));
            }
        }
        finally
        {
            foreach (SKPaint paint in strokes.Values)
            {
                paint.Dispose();
            }
        }
    }

    private SKPaint GetRoadStroke(
        Dictionary<(GsiRoadKind Kind, float Width, bool Casing), SKPaint> strokes,
        GsiRoadKind kind,
        float width,
        float tileScale,
        bool casing)
    {
        var key = (kind, width, casing);
        if (!strokes.TryGetValue(key, out SKPaint? paint))
        {
            paint = StrokeOf(casing ? this.GetCasingPaint(kind) : this.GetFillPaint(kind), width / tileScale);
            strokes.Add(key, paint);
        }

        return paint;
    }

    private static SKPaint StrokeOf(SKPaint template, float strokeWidth)
    {
        SKPaint paint = template.Clone();
        paint.StrokeWidth = strokeWidth;
        return paint;
    }

    /// <summary>
    /// ベクターフィーチャーの属性から道路種別と幅員を取得します。
    /// </summary>
    /// <param name="feature">道路フィーチャー。</param>
    /// <returns>道路種別と描画基準幅。</returns>
    public static (GsiRoadKind Kind, float Width) GetRoadCategory(VectorTileFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        // 国土地理院 最適化ベクトルタイル現行仕様: vt_rdctg / vt_rnkwidth (公式 std.json 準拠)
        // vt_rdctg: "高速自動車国道等", "国道", "主要道路", "都道府県道", "市区町村道等", "その他"
        // コード値 (フォールバック): 3: 高速自動車国道等, 0: 国道, 1: 都道府県道, 2: 市区町村道, 5: その他
        string? rdCtgStr = feature.GetString("vt_rdctg") ?? feature.GetString("rdCtg");
        long? rdCtgNum = feature.GetInt64("vt_rdctg") ?? feature.GetInt64("rdCtg");

        GsiRoadKind kind = GsiRoadKind.Minor;
        if (!string.IsNullOrEmpty(rdCtgStr))
        {
            string trimmed = rdCtgStr.Trim();
            switch (trimmed)
            {
                case "高速自動車国道等":
                    kind = GsiRoadKind.Highway;
                    break;
                case "国道":
                case "主要道路":
                    kind = GsiRoadKind.National;
                    break;
                case "都道府県道":
                    kind = GsiRoadKind.Prefectural;
                    break;
                case "市区町村道等":
                case "その他":
                    kind = GsiRoadKind.Minor;
                    break;
                default:
                    if (trimmed.Contains("高速", StringComparison.Ordinal))
                    {
                        kind = GsiRoadKind.Highway;
                    }
                    else if (trimmed.Contains("国道", StringComparison.Ordinal) || trimmed.Contains("主要道路", StringComparison.Ordinal))
                    {
                        kind = GsiRoadKind.National;
                    }
                    else if (trimmed.Contains("都道府県道", StringComparison.Ordinal))
                    {
                        kind = GsiRoadKind.Prefectural;
                    }
                    else if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedNum))
                    {
                        kind = MapNumericRoadCategory(parsedNum);
                    }
                    break;
            }
        }
        else if (rdCtgNum.HasValue)
        {
            kind = MapNumericRoadCategory(rdCtgNum.Value);
        }

        float baseWidth = ParseRoadWidth(feature);

        return kind switch
        {
            GsiRoadKind.Highway => (GsiRoadKind.Highway, Math.Max(baseWidth, 3.8f)),
            GsiRoadKind.National => (GsiRoadKind.National, Math.Max(baseWidth, 3.2f)),
            GsiRoadKind.Prefectural => (GsiRoadKind.Prefectural, Math.Max(baseWidth, 2.8f)),
            _ => (GsiRoadKind.Minor, baseWidth),
        };
    }

    private static GsiRoadKind MapNumericRoadCategory(long rdCtg) => rdCtg switch
    {
        3 => GsiRoadKind.Highway,
        0 => GsiRoadKind.National,
        1 => GsiRoadKind.Prefectural,
        _ => GsiRoadKind.Minor,
    };

    private static float ParseRoadWidth(VectorTileFeature feature)
    {
        // 国土地理院 最適化ベクトルタイル現行仕様: vt_rnkwidth (公式 std.json 準拠)
        // vt_rnkwidth: "3m未満", "3m-5.5m未満", "5.5m-13m未満", "13m-19.5m未満", "19.5m以上", "不明"
        // ランク値 (フォールバック): 0: 3m未満, 1: 3m-5.5m未満, 2: 5.5m-13m未満, 3: 13m-19.5m未満, 4: 19.5m以上
        string? rnkStr = feature.GetString("vt_rnkwidth") ?? feature.GetString("rnkWidth");
        if (!string.IsNullOrEmpty(rnkStr))
        {
            string trimmed = rnkStr.Trim();
            switch (trimmed)
            {
                case "3m未満":
                    return 1.5f;
                case "3m-5.5m未満":
                    return 2.2f;
                case "5.5m-13m未満":
                    return 3.2f;
                case "13m-19.5m未満":
                    return 4.2f;
                case "19.5m以上":
                    return 5.2f;
                case "不明":
                    return 2.2f;
                default:
                    if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long num))
                    {
                        return MapNumericRankWidth(num);
                    }
                    break;
            }
        }

        long? numVal = feature.GetInt64("vt_rnkwidth") ?? feature.GetInt64("rnkWidth");
        if (numVal.HasValue)
        {
            return MapNumericRankWidth(numVal.Value);
        }

        return 2.2f;
    }

    private static float MapNumericRankWidth(long rank) => rank switch
    {
        0 => 1.5f,
        1 => 2.2f,
        2 => 3.2f,
        3 => 4.2f,
        _ => 5.2f,
    };

    private SKPaint GetCasingPaint(GsiRoadKind kind) => kind switch
    {
        GsiRoadKind.Highway => this.highwayCasing,
        GsiRoadKind.National => this.nationalCasing,
        GsiRoadKind.Prefectural => this.prefCasing,
        _ => this.minorCasing,
    };

    private SKPaint GetFillPaint(GsiRoadKind kind) => kind switch
    {
        GsiRoadKind.Highway => this.highwayFill,
        GsiRoadKind.National => this.nationalFill,
        GsiRoadKind.Prefectural => this.prefFill,
        _ => this.minorFill,
    };

    private static void RenderLayerPolygons(
        SKCanvas canvas,
        VectorTileLayer? layer,
        SKPaint fillPaint,
        SKPaint? strokePaint = null)
    {
        if (layer is null)
        {
            return;
        }

        for (int i = 0; i < layer.Features.Count; i++)
        {
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.Polygon)
            {
                continue;
            }

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
    }

    private static void RenderLayerLines(SKCanvas canvas, VectorTileLayer? layer, SKPaint strokePaint)
    {
        if (layer is null)
        {
            return;
        }

        for (int i = 0; i < layer.Features.Count; i++)
        {
            RenderFeatureLine(canvas, layer.Features[i], strokePaint);
        }
    }

    private static void RenderFeatureLine(SKCanvas canvas, VectorTileFeature feature, SKPaint strokePaint)
    {
        if (feature.GeometryType != VectorGeometryType.LineString)
        {
            return;
        }

        using var builder = new SKPathBuilder();
        for (int part = 0; part < feature.GeometryParts.Count; part++)
        {
            IReadOnlyList<VectorPoint> line = feature.GeometryParts[part];
            if (line.Count < 2)
            {
                continue;
            }

            builder.MoveTo(line[0].X, line[0].Y);
            for (int p = 1; p < line.Count; p++)
            {
                builder.LineTo(line[p].X, line[p].Y);
            }
        }

        using SKPath path = builder.Detach();
        canvas.DrawPath(path, strokePaint);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.waterPaint.Dispose();
        this.greenAreaPaint.Dispose();
        this.buildingFillPaint.Dispose();
        this.buildingStrokePaint.Dispose();
        this.contourPaint.Dispose();
        this.riverPaint.Dispose();
        this.railwayPaint.Dispose();
        this.admBdryPaint.Dispose();
        this.highwayCasing.Dispose();
        this.highwayFill.Dispose();
        this.nationalCasing.Dispose();
        this.nationalFill.Dispose();
        this.prefCasing.Dispose();
        this.prefFill.Dispose();
        this.minorCasing.Dispose();
        this.minorFill.Dispose();
        this.labelFont.Dispose();
        this.labelTypeface.Dispose();
        this.labelFillPaint.Dispose();
        this.labelHaloPaint.Dispose();
    }
}
