using System.Globalization;
using System.Net;

using PhotoMapStudio.Core.Geo;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// ベクタータイルから SkiaSharp キャンバスへ直接地図を描画するレンダラー。
/// </summary>
public sealed class VectorMapRenderer : IVectorMapRenderer
{
    private readonly IVectorTileSource defaultTileSource;
    private readonly IVectorMapStyle defaultStyle;
    private readonly Func<TileSource?, (IVectorTileSource Source, IVectorMapStyle Style)>? resolver;

    /// <summary>
    /// 単一のデータソースとスタイルでベクター地図レンダラーを初期化する。
    /// </summary>
    /// <param name="tileSource">ベクタータイルデータソース。</param>
    /// <param name="style">地図描画スタイル。</param>
    public VectorMapRenderer(IVectorTileSource tileSource, IVectorMapStyle style)
    {
        this.defaultTileSource = tileSource ?? throw new ArgumentNullException(nameof(tileSource));
        this.defaultStyle = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>
    /// タイル配信元に応じて動的にデータソースとスタイルを切り替えるリゾルバーを指定して初期化する。
    /// </summary>
    /// <param name="defaultTileSource">既定のベクタータイルデータソース。</param>
    /// <param name="defaultStyle">既定の地図描画スタイル。</param>
    /// <param name="resolver">タイル配信元に応じたソースおよびスタイルの解決関数。</param>
    public VectorMapRenderer(
        IVectorTileSource defaultTileSource,
        IVectorMapStyle defaultStyle,
        Func<TileSource?, (IVectorTileSource Source, IVectorMapStyle Style)> resolver)
        : this(defaultTileSource, defaultStyle)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <inheritdoc />
    public Task<SKBitmap> RenderMapAsync(
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        CancellationToken cancellationToken = default)
        => this.RenderMapAsync(center, zoom, width, height, session: null, tileSource: null, cancellationToken);

    /// <inheritdoc />
    public Task<SKBitmap> RenderMapAsync(
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        TileFetchSession? session,
        CancellationToken cancellationToken = default)
        => this.RenderMapAsync(center, zoom, width, height, session, tileSource: null, cancellationToken);

    /// <inheritdoc />
    public async Task<SKBitmap> RenderMapAsync(
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        TileFetchSession? session,
        TileSource? tileSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        SKBitmap? bitmap = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(bitmap);
            await this.RenderToCanvasAsync(canvas, center, zoom, width, height, session, tileSource, cancellationToken).ConfigureAwait(false);

            SKBitmap rendered = bitmap;
            bitmap = null;
            return rendered;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    /// <inheritdoc />
    public Task RenderToCanvasAsync(
        SKCanvas canvas,
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        CancellationToken cancellationToken = default)
        => this.RenderToCanvasAsync(canvas, center, zoom, width, height, session: null, tileSource: null, cancellationToken);

    /// <inheritdoc />
    public Task RenderToCanvasAsync(
        SKCanvas canvas,
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        TileFetchSession? session,
        CancellationToken cancellationToken = default)
        => this.RenderToCanvasAsync(canvas, center, zoom, width, height, session, tileSource: null, cancellationToken);

    /// <inheritdoc />
    public async Task RenderToCanvasAsync(
        SKCanvas canvas,
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        TileFetchSession? session,
        TileSource? tileSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(zoom);

        var (activeSource, activeStyle) = this.resolver != null
            ? this.resolver(tileSource)
            : (this.defaultTileSource, this.defaultStyle);

        // 1. タイル取得用ズームとスケール比の決定（過拡大対応）
        int tileZoom = Math.Min(zoom, activeSource.MaxZoom);
        double scaleRatio = Math.Pow(2.0, zoom - tileZoom);
        int maxTiles = 1 << tileZoom;

        // 2. 世界ピクセル座標系での表示範囲計算
        TilePoint centerTilePoint = WebMercator.ToTilePoint(center, zoom);
        double left = centerTilePoint.WorldPixelX - (width / 2.0);
        double top = centerTilePoint.WorldPixelY - (height / 2.0);
        double right = left + width;
        double bottom = top + height;

        // 3. 必要なタイルの座標範囲計算
        double tileLeft = left / scaleRatio;
        double tileTop = top / scaleRatio;
        double tileRight = right / scaleRatio;
        double tileBottom = bottom / scaleRatio;

        int minTx = (int)Math.Floor(tileLeft / WebMercator.TileSize);
        int minTy = (int)Math.Floor(tileTop / WebMercator.TileSize);
        int maxTx = (int)Math.Floor(tileRight / WebMercator.TileSize);
        int maxTy = (int)Math.Floor(tileBottom / WebMercator.TileSize);

        // 4. タイルの非同期取得とデコード
        var loadedTiles = new List<(int tx, int ty, VectorTile tile, float scale, float dx, float dy)>();
        int attemptedTiles = 0;
        int firstFetchX = 0;
        int firstFetchY = 0;

        for (int ty = minTy; ty <= maxTy; ty++)
        {
            // Y 座標は極地方向（上下）でループしないため、有効範囲外はスキップ
            if (ty < 0 || ty >= maxTiles)
            {
                continue;
            }

            for (int tx = minTx; tx <= maxTx; tx++)
            {
                // X 座標は経度方向（左右）で世界一周 wrap する
                int fetchX = ((tx % maxTiles) + maxTiles) % maxTiles;

                if (attemptedTiles++ == 0)
                {
                    firstFetchX = fetchX;
                    firstFetchY = ty;
                }

                byte[]? tileData = await activeSource.GetTileAsync(tileZoom, fetchX, ty, session, cancellationToken).ConfigureAwait(false);
                if (tileData is null || tileData.Length == 0)
                {
                    continue;
                }

                VectorTile tile = VectorTileDecoder.Decode(tileData);
                uint extent = 4096;
                if (tile.Layers.Count > 0)
                {
                    extent = tile.Layers[0].Extent;
                }

                float scale = (float)((WebMercator.TileSize * scaleRatio) / extent);
                float dx = (float)((tx * WebMercator.TileSize * scaleRatio) - left);
                float dy = (float)((ty * WebMercator.TileSize * scaleRatio) - top);

                loadedTiles.Add((tx, ty, tile, scale, dx, dy));
            }
        }

        // 描画できるタイルが 1 枚もない場合は配信範囲外として扱う。白紙の地図を成功として返すと、
        // 国外への切替（404 のみが契機）が働かず、一括生成でも異常に気付けない。
        if (attemptedTiles > 0 && loadedTiles.Count == 0)
        {
            Uri requestUri = tileSource?.BuildTileUri(tileZoom, firstFetchX, firstFetchY)
                ?? new Uri(string.Create(CultureInfo.InvariantCulture, $"vector-tile:{activeSource.SourceId}/{tileZoom}/{firstFetchX}/{firstFetchY}"), UriKind.Absolute);
            throw new TileFetchException(
                string.Create(CultureInfo.InvariantCulture, $"この地点には描画できるベクタータイルがありません（配信範囲外の可能性があります）: ズーム {tileZoom}"),
                requestUri,
                HttpStatusCode.NotFound,
                null);
        }

        // 5. SkiaSharp によるキャンバス描画
        // 背景クリア
        canvas.Clear(activeStyle.BackgroundColor);

        var viewport = new SKRect(0, 0, width, height);
        var collisionGrid = new LabelCollisionGrid();

        // パス 1: 全タイルの面（ポリゴン）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            var item = loadedTiles[i];
            canvas.Save();
            canvas.Translate(item.dx, item.dy);
            canvas.Scale(item.scale, item.scale);

            activeStyle.RenderPolygons(canvas, item.tile, item.scale, zoom);

            canvas.Restore();
        }

        // パス 2: 全タイルの線（道路・鉄道・ライン）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            var item = loadedTiles[i];
            canvas.Save();
            canvas.Translate(item.dx, item.dy);
            canvas.Scale(item.scale, item.scale);

            activeStyle.RenderLines(canvas, item.tile, item.scale, zoom);

            canvas.Restore();
        }

        // パス 3a: 全タイルのシンボル（経路番号の盾など）。注記より優先して配置する
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            var item = loadedTiles[i];
            SKMatrix tileToCanvasMatrix = SKMatrix.CreateTranslation(item.dx, item.dy);
            tileToCanvasMatrix = tileToCanvasMatrix.PreConcat(SKMatrix.CreateScale(item.scale, item.scale));

            activeStyle.RenderSymbols(canvas, item.tile, tileToCanvasMatrix, zoom, collisionGrid, viewport);
        }

        // パス 3b: 全タイルの注記・ラベル（画面座標系で直接描画）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            var item = loadedTiles[i];
            SKMatrix tileToCanvasMatrix = SKMatrix.CreateTranslation(item.dx, item.dy);
            tileToCanvasMatrix = tileToCanvasMatrix.PreConcat(SKMatrix.CreateScale(item.scale, item.scale));

            activeStyle.RenderLabels(canvas, item.tile, tileToCanvasMatrix, zoom, collisionGrid, viewport);
        }
    }
}
