using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// OpenMapTiles / OpenFreeMap スキーマに対応した標準ベクター地図スタイル。
/// </summary>
public sealed class OpenMapTilesStyle : IVectorMapStyle, IDisposable
{
    /// <inheritdoc />
    public string StyleId => "openmaptiles";

    /// <inheritdoc />
    public SKColor BackgroundColor => new(0xF2, 0xEF, 0xE9); // #F2EFE9

    // 面（ポリゴン）用ペイント
    private readonly SKPaint waterPaint = new()
    {
        Color = new SKColor(0xA5, 0xD6, 0xE7), // #A5D6E7
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint parkPaint = new()
    {
        Color = new SKColor(0xD0, 0xE7, 0xD2), // #D0E7D2
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint urbanLandusePaint = new()
    {
        Color = new SKColor(0xE8, 0xE4, 0xDE), // #E8E4DE
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint buildingFillPaint = new()
    {
        Color = new SKColor(0xE0, 0xDC, 0xD6), // #E0DCD6
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private readonly SKPaint buildingStrokePaint = new()
    {
        Color = new SKColor(0xD2, 0xCD, 0xC6), // #D2CDC6
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
    private readonly SKFont placeFont;

    /// <summary>
    /// OpenMapTiles ベクタースタイルを初期化する。
    /// </summary>
    public OpenMapTilesStyle()
    {
        this.placeFont = new SKFont(this.placeTypeface, 12f);
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

    /// <inheritdoc />
    public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tile);

        // 1. 緑地・土地利用（landcover / landuse / park）
        RenderLayerPolygons(canvas, tile.GetLayer("landcover"), this.parkPaint);
        RenderLayerPolygons(canvas, tile.GetLayer("park"), this.parkPaint);
        RenderLayerPolygons(canvas, tile.GetLayer("landuse"), this.urbanLandusePaint);

        // 2. 水域（water）
        RenderLayerPolygons(canvas, tile.GetLayer("water"), this.waterPaint);

        // 3. 建物（building）: ズーム 13 以上で描画
        if (zoom >= 13)
        {
            VectorTileLayer? buildingLayer = tile.GetLayer("building");
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
        using SKPaint railway = this.railwayPaint.Clone();
        railway.StrokeWidth = 1.2f / tileScale;

        VectorTileLayer? roadLayer = tile.GetLayer("transportation");
        if (roadLayer is not null)
        {
            // 1. 鉄道（transportation 内の class="rail", "transit", "subway", "tram"）
            RenderTransportationRailways(canvas, roadLayer, railway);

            // 2. 道路（motorway, primary, secondary 等）
            RenderRoads(canvas, roadLayer, tileScale, zoom);
        }

        // フォールバック: 別途 railway レイヤーが存在する場合も描画
        VectorTileLayer? fallbackRailwayLayer = tile.GetLayer("railway");
        if (fallbackRailwayLayer is not null)
        {
            RenderLayerLines(canvas, fallbackRailwayLayer, railway);
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

        // 1. 市区町村・地名（place）
        VectorTileLayer? placeLayer = tile.GetLayer("place");
        if (placeLayer is not null)
        {
            for (int i = 0; i < placeLayer.Features.Count; i++)
            {
                VectorTileFeature feature = placeLayer.Features[i];
                if (feature.GeometryType != VectorGeometryType.Point || feature.GeometryParts.Count == 0)
                {
                    continue;
                }

                string? name = feature.GetString("name:ja") ?? feature.GetString("name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                VectorPoint pt = feature.GeometryParts[0][0];
                SKPoint canvasPt = tileToCanvasMatrix.MapPoint(new SKPoint(pt.X, pt.Y));

                float textWidth = this.placeFont.MeasureText(name);
                var bounds = new SKRect(
                    canvasPt.X - (textWidth / 2f),
                    canvasPt.Y - 12f,
                    canvasPt.X + (textWidth / 2f),
                    canvasPt.Y + 4f);

                if (collisionGrid.TryPlace(bounds, viewport))
                {
                    // 白ハロー（縁取り）を描画
                    canvas.DrawText(name, bounds.Left, canvasPt.Y, SKTextAlign.Left, this.placeFont, this.textHaloPaint);
                    // 本文を描画
                    canvas.DrawText(name, bounds.Left, canvasPt.Y, SKTextAlign.Left, this.placeFont, this.placeFillPaint);
                }
            }
        }

        // 2. 施設（poi）: ズーム 14 以上
        if (zoom >= 14)
        {
            VectorTileLayer? poiLayer = tile.GetLayer("poi");
            if (poiLayer is not null)
            {
                for (int i = 0; i < poiLayer.Features.Count; i++)
                {
                    VectorTileFeature feature = poiLayer.Features[i];
                    if (feature.GeometryType != VectorGeometryType.Point || feature.GeometryParts.Count == 0)
                    {
                        continue;
                    }

                    string? name = feature.GetString("name:ja") ?? feature.GetString("name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    VectorPoint pt = feature.GeometryParts[0][0];
                    SKPoint canvasPt = tileToCanvasMatrix.MapPoint(new SKPoint(pt.X, pt.Y));

                    float textWidth = this.placeFont.MeasureText(name);
                    var bounds = new SKRect(
                        canvasPt.X - (textWidth / 2f),
                        canvasPt.Y - 10f,
                        canvasPt.X + (textWidth / 2f),
                        canvasPt.Y + 4f);

                    if (collisionGrid.TryPlace(bounds, viewport))
                    {
                        canvas.DrawText(name, bounds.Left, canvasPt.Y, SKTextAlign.Left, this.placeFont, this.textHaloPaint);
                        canvas.DrawText(name, bounds.Left, canvasPt.Y, SKTextAlign.Left, this.placeFont, this.placeFillPaint);
                    }
                }
            }
        }
    }

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

    private static void RenderLayerLines(SKCanvas canvas, VectorTileLayer layer, SKPaint paint)
    {
        for (int i = 0; i < layer.Features.Count; i++)
        {
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
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

    private static void RenderTransportationRailways(SKCanvas canvas, VectorTileLayer layer, SKPaint paint)
    {
        for (int i = 0; i < layer.Features.Count; i++)
        {
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
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

    private static void RenderRoads(SKCanvas canvas, VectorTileLayer layer, float tileScale, int zoom)
    {
        var roadFeatures = new List<(VectorTileFeature feature, int order, SKColor casingColor, SKColor fillColor, float casingWidth, float fillWidth)>();

        for (int i = 0; i < layer.Features.Count; i++)
        {
            VectorTileFeature feature = layer.Features[i];
            if (feature.GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            string roadClass = feature.GetString("class") ?? string.Empty;
            var (order, casingColor, fillColor, casingWidth, fillWidth) = GetRoadStyle(roadClass, zoom);
            if (fillWidth > 0f)
            {
                roadFeatures.Add((feature, order, casingColor, fillColor, casingWidth, fillWidth));
            }
        }

        // 描画順序（下位階層 -> 上位階層）でソート
        roadFeatures.Sort((a, b) => a.order.CompareTo(b.order));

        // パス 1: 外枠（casing）の描画
        for (int i = 0; i < roadFeatures.Count; i++)
        {
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
        return roadClass switch
        {
            "motorway" or "trunk" => (
                50,
                new SKColor(0xCE, 0x80, 0x46),
                new SKColor(0xF9, 0xB2, 0x9C),
                7.5f,
                5.0f),

            "primary" => (
                40,
                new SKColor(0xC8, 0x8E, 0x4B),
                new SKColor(0xFE, 0xD3, 0x82),
                6.0f,
                4.0f),

            "secondary" => (
                30,
                new SKColor(0xA0, 0xA0, 0xA0),
                SKColors.White,
                4.5f,
                3.0f),

            "tertiary" or "minor" or "residential" => (
                20,
                new SKColor(0xC0, 0xC0, 0xC0),
                SKColors.White,
                3.5f,
                2.0f),

            "service" or "track" or "path" => (
                10,
                SKColors.Transparent,
                new SKColor(0xDC, 0xDC, 0xDC),
                0f,
                1.5f),

            _ => (0, SKColors.Transparent, SKColors.Transparent, 0f, 0f),
        };
    }

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
        this.urbanLandusePaint.Dispose();
        this.buildingFillPaint.Dispose();
        this.buildingStrokePaint.Dispose();
        this.railwayPaint.Dispose();
        this.placeFont.Dispose();
        this.placeTypeface.Dispose();
        this.placeFillPaint.Dispose();
        this.textHaloPaint.Dispose();
    }
}
