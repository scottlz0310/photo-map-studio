using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 国土地理院の最適化ベクトルタイル（optimal_bvmap-v1）スキーマに対応した標準ベクター地図スタイル。
/// 地理院地形図・淡色地図調の配色（水域・等高線・道路階層・建物・注記）を再現します。
/// </summary>
public sealed class GsiVectorStyle : IVectorMapStyle, IDisposable
{
    private enum GsiRoadKind
    {
        Highway,
        National,
        Prefectural,
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
    private readonly SKFont labelFont = new(SKTypeface.Default, 11f);
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

        // 1. 等高線 (Cntr)
        this.contourPaint.StrokeWidth = 0.8f / tileScale;
        RenderLayerLines(canvas, tile.GetLayer("Cntr"), this.contourPaint);

        // 2. 水涯線・河川中心線・海岸線 (WL / RvrCL / Cstline)
        this.riverPaint.StrokeWidth = 1.0f / tileScale;
        RenderLayerLines(canvas, tile.GetLayer("WL"), this.riverPaint);
        RenderLayerLines(canvas, tile.GetLayer("RvrCL"), this.riverPaint);
        RenderLayerLines(canvas, tile.GetLayer("Cstline"), this.riverPaint);

        // 3. 行政境界線 (AdmBdry)
        this.admBdryPaint.StrokeWidth = 1.0f / tileScale;
        RenderLayerLines(canvas, tile.GetLayer("AdmBdry"), this.admBdryPaint);

        // 4. 鉄道 (RailCL)
        this.railwayPaint.StrokeWidth = 1.2f / tileScale;
        RenderLayerLines(canvas, tile.GetLayer("RailCL"), this.railwayPaint);

        // 5. 道路 (RdCL: 道路中心線 / RdEdg: 道路縁)
        VectorTileLayer? roadLayer = tile.GetLayer("RdCL");
        if (roadLayer is not null)
        {
            this.RenderRoads(canvas, roadLayer, tileScale);
        }

        VectorTileLayer? roadEdgeLayer = tile.GetLayer("RdEdg");
        if (roadEdgeLayer is not null && zoom >= 15)
        {
            this.minorCasing.StrokeWidth = 0.8f / tileScale;
            RenderLayerLines(canvas, roadEdgeLayer, this.minorCasing);
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
            if (feature.GeometryParts.Count == 0 || feature.GeometryParts[0].Count == 0)
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
        // 道路は casing (外枠) を先にすべて描き、その上に fill (中塗り) を描く
        for (int i = 0; i < roadLayer.Features.Count; i++)
        {
            VectorTileFeature feature = roadLayer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            var (kind, width) = GetRoadCategory(feature);
            SKPaint casing = this.GetCasingPaint(kind);
            casing.StrokeWidth = (width + 1.6f) / tileScale;
            RenderFeatureLine(canvas, feature, casing);
        }

        for (int i = 0; i < roadLayer.Features.Count; i++)
        {
            VectorTileFeature feature = roadLayer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            var (kind, width) = GetRoadCategory(feature);
            SKPaint fill = this.GetFillPaint(kind);
            fill.StrokeWidth = width / tileScale;
            RenderFeatureLine(canvas, feature, fill);
        }
    }

    private static (GsiRoadKind Kind, float Width) GetRoadCategory(VectorTileFeature feature)
    {
        // 国土地理院 最適化ベクトルタイル現行仕様: vt_rdctg / vt_rnkwidth (公式 std.json 準拠)
        // 0: 国道, 1: 都道府県道, 2: 市区町村道, 3: 高速自動車国道等, 5: その他
        long rdCtg = feature.GetInt64("vt_rdctg")
            ?? feature.GetInt64("rdCtg")
            ?? 2;
        long rnkWidth = feature.GetInt64("vt_rnkwidth")
            ?? feature.GetInt64("rnkWidth")
            ?? 1;

        float baseWidth = rnkWidth switch
        {
            0 => 1.5f,
            1 => 2.2f,
            2 => 3.2f,
            3 => 4.2f,
            _ => 5.2f,
        };

        return rdCtg switch
        {
            3 => (GsiRoadKind.Highway, Math.Max(baseWidth, 3.8f)),
            0 => (GsiRoadKind.National, Math.Max(baseWidth, 3.2f)),
            1 => (GsiRoadKind.Prefectural, Math.Max(baseWidth, 2.8f)),
            _ => (GsiRoadKind.Minor, baseWidth),
        };
    }

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
        this.labelFillPaint.Dispose();
        this.labelHaloPaint.Dispose();
    }
}
