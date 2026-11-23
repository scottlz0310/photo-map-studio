using System.Globalization;
using System.Net;

using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// ベクタータイルから SkiaSharp キャンバスへ直接地図を描画するレンダラー。
/// </summary>
public sealed class VectorMapRenderer : IVectorMapRenderer, IVectorMapPrefetcher
{
    private readonly IVectorTileSource defaultTileSource;
    private readonly IVectorMapStyle defaultStyle;
    private readonly Func<TileSource?, (IVectorTileSource Source, IVectorMapStyle Style)>? resolver;
    private readonly DecodedTileCache? decodedTileCache;

    /// <summary>
    /// 単一のデータソースとスタイルでベクター地図レンダラーを初期化する。
    /// </summary>
    /// <param name="tileSource">ベクタータイルデータソース。</param>
    /// <param name="style">地図描画スタイル。</param>
    /// <param name="decodedTileCache">デコード済みタイルのキャッシュ。指定すると、同じタイルの取得とデコードを省く。</param>
    public VectorMapRenderer(IVectorTileSource tileSource, IVectorMapStyle style, DecodedTileCache? decodedTileCache = null)
    {
        this.defaultTileSource = tileSource ?? throw new ArgumentNullException(nameof(tileSource));
        this.defaultStyle = style ?? throw new ArgumentNullException(nameof(style));
        this.decodedTileCache = decodedTileCache;
    }

    /// <summary>
    /// タイル配信元に応じて動的にデータソースとスタイルを切り替えるリゾルバーを指定して初期化する。
    /// </summary>
    /// <param name="defaultTileSource">既定のベクタータイルデータソース。</param>
    /// <param name="defaultStyle">既定の地図描画スタイル。</param>
    /// <param name="resolver">タイル配信元に応じたソースおよびスタイルの解決関数。</param>
    /// <param name="decodedTileCache">デコード済みタイルのキャッシュ。指定すると、同じタイルの取得とデコードを省く。</param>
    public VectorMapRenderer(
        IVectorTileSource defaultTileSource,
        IVectorMapStyle defaultStyle,
        Func<TileSource?, (IVectorTileSource Source, IVectorMapStyle Style)> resolver,
        DecodedTileCache? decodedTileCache = null)
        : this(defaultTileSource, defaultStyle, decodedTileCache)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>
    /// 表示範囲全体を 1 枚の画像として描く。ズームは連続値で、位置・拡大率は連続値のまま描き、
    /// 取得するタイルのズームだけを整数にする。画像は呼び出し側へ所有権を渡す（共有・再利用しない）。
    /// </summary>
    /// <param name="request">描画要求（中心・連続ズーム・物理ピクセル寸法など）。</param>
    /// <param name="cancellationToken">
    /// キャンセルトークン。取得・描画の途中で確認し、キャンセルされたときは画像を返さず
    /// <see cref="OperationCanceledException"/> で終わる。
    /// </param>
    /// <returns>画像と欠落タイルの集計。使い終わったら <see cref="VectorViewResult.Dispose"/> する（画像を取り出したときは不要）。</returns>
    /// <exception cref="TileFetchException">
    /// 描画できるタイルが 1 枚もない、または欠落を許可していないときに取得できないタイルがある。
    /// </exception>
    /// <inheritdoc />
    public async Task<VectorViewResult> RenderViewAsync(VectorViewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.PixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.PixelHeight);
        if (!double.IsFinite(request.Zoom) || request.Zoom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Zoom, "ズームは 0 以上の有限値で指定してください。");
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(request.BandCount, 1, nameof(request));

        // 範囲の外（極端に小さい・大きい値）では、論理寸法・座標が非有限になったり、Skia へ渡す float の倍率が 0 や無限大になったりして、
        // 背景だけの画像が成功として返る。描画の前に、有効な範囲として検証する（NaN も範囲外になる）
        if (!(request.PixelRatio >= VectorViewRequest.MinPixelRatio && request.PixelRatio <= VectorViewRequest.MaxPixelRatio))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.PixelRatio,
                string.Create(CultureInfo.InvariantCulture, $"PixelRatio は {VectorViewRequest.MinPixelRatio} 以上 {VectorViewRequest.MaxPixelRatio} 以下で指定してください。"));
        }

        var context = new VectorMapRenderContext(request.AllowIncompleteMaps);
        using var bitmap = new SKBitmap(request.PixelWidth, request.PixelHeight, request.ColorType ?? SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            await this.RenderCoreAsync(
                canvas, request.CenterLatitude, request.CenterLongitude, request.Zoom, request.PixelRatio, request.PixelWidth, request.PixelHeight,
                request.Session, request.TileSource, context,
                request.BandCount > 1 || request.ParallelLabels ? new ParallelDraw(bitmap, request.BandCount, request.ParallelLabels) : null,
                cancellationToken).ConfigureAwait(false);
        }

        // 画像を不変にしてから SKImage へ渡す。画素は共有し、コピーしない
        bitmap.SetImmutable();
        return new VectorViewResult(SKImage.FromBitmap(bitmap), context.RequestedTileCount, context.UnavailableTileCount, context.AbsentTileCount);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">デコード済みキャッシュを指定していない（入れる先がなく、効果がない）。</exception>
    /// <exception cref="TileFetchException">取得できないタイルがある、または取得した内容をベクタータイルとして読めない。</exception>
    public async Task PrefetchAsync(VectorPrefetchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!double.IsFinite(request.Zoom) || request.Zoom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Zoom, "ズームは 0 以上の有限値で指定してください。");
        }

        if (this.decodedTileCache is null)
        {
            throw new InvalidOperationException("事前デコードには、デコード済みキャッシュ（DecodedTileCache）を指定した VectorMapRenderer が必要です。");
        }

        var (activeSource, _) = this.resolver != null
            ? this.resolver(request.TileSource)
            : (this.defaultTileSource, this.defaultStyle);

        cancellationToken.ThrowIfCancellationRequested();
        await activeSource.PrepareAsync(request.Session, cancellationToken).ConfigureAwait(false);
        int tileZoom = Math.Min((int)Math.Floor(request.Zoom), activeSource.MaxZoom);

        foreach ((int x, int y) in PrefetchTileSelector.Around(request.CenterLatitude, request.CenterLongitude, tileZoom))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 欠落（配信範囲外）のタイルは、入れるものがないので飛ばす。取得の失敗・読めない内容は、最適化の失敗として呼び出し側へ返す
            TileLoad load = await this.LoadTileAsync(activeSource, tileZoom, x, y, request.Session, allowIncompleteMaps: false, cancellationToken).ConfigureAwait(false);
            if (load.Status == TileLoadStatus.Unreadable)
            {
                throw CreateUnreadableTileException(BuildRequestUri(activeSource, request.TileSource, tileZoom, x, y));
            }
        }

        // 最後のタイルの処理中にキャンセルされた場合も、正常完了として返さない
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task RenderCoreAsync(SKCanvas canvas, double latitude, double longitude, double zoom, double pixelRatio, int width, int height,
        TileFetchSession? session, TileSource? tileSource, VectorMapRenderContext renderContext,
        ParallelDraw? parallel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(renderContext);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var (activeSource, activeStyle) = this.resolver != null
            ? this.resolver(tileSource)
            : (this.defaultTileSource, this.defaultStyle);

        // 1. タイル取得用ズームとスケール比の決定（過拡大対応）。最大ズームを確定してから求める。
        // 取得ズームは整数、表示倍率は連続値。スタイルの判定には、画素密度を除いた論理ズームを切り捨てた整数を使う。
        // 取得するタイルの範囲は、物理ピクセルで求める（画素密度に依らず、従来と同じ計算。論理ピクセルへ変換してから求めると、
        // タイルの境界ちょうどの範囲で浮動小数点の誤差が出て、画面外のタイルを余分に取得し得る）。
        // 描くときの位置・拡大率・衝突判定の範囲だけを論理ピクセルへ換算し、キャンバスを画素密度倍に拡大して描く
        // （スタイルの大きさが論理ピクセルで一定になる）
        cancellationToken.ThrowIfCancellationRequested();
        await activeSource.PrepareAsync(session, cancellationToken).ConfigureAwait(false);
        int styleZoom = Math.Max(0, (int)Math.Floor(zoom - Math.Log2(pixelRatio)));
        int tileZoom = Math.Min((int)Math.Floor(zoom), activeSource.MaxZoom);
        double scaleRatio = Math.Pow(2.0, zoom - tileZoom);
        int maxTiles = 1 << tileZoom;
        double logicalWidth = width / pixelRatio;
        double logicalHeight = height / pixelRatio;

        // 2. 世界ピクセル座標系（物理ピクセル）での表示範囲計算
        (double centerWorldX, double centerWorldY) = MercatorProjection.ToWorldPixel(latitude, longitude, zoom);
        double left = centerWorldX - (width / 2.0);
        double top = centerWorldY - (height / 2.0);
        double right = left + width;
        double bottom = top + height;

        // 3. 必要なタイルの座標範囲計算
        double tileLeft = left / scaleRatio;
        double tileTop = top / scaleRatio;
        double tileRight = right / scaleRatio;
        double tileBottom = bottom / scaleRatio;

        int minTx = (int)Math.Floor(tileLeft / MercatorProjection.TileSize);
        int minTy = (int)Math.Floor(tileTop / MercatorProjection.TileSize);
        int maxTx = (int)Math.Floor(tileRight / MercatorProjection.TileSize);
        int maxTy = (int)Math.Floor(tileBottom / MercatorProjection.TileSize);

        // 4. タイルの非同期取得とデコード
        var loadedTiles = new List<(int tx, int ty, VectorTile tile, float scale, float dx, float dy)>();

        // タイルを、表示範囲の画素へ置く拡大率と位置を決める。物理ピクセルの値を画素密度で割り、論理ピクセル（キャンバスの拡大の前の座標）にする
        (int Tx, int Ty, VectorTile Tile, float Scale, float Dx, float Dy) Place(VectorTile placed, int tx, int ty)
        {
            uint extent = placed.Layers.Count > 0 ? placed.Layers[0].Extent : 4096;
            float scale = (float)((MercatorProjection.TileSize * scaleRatio) / extent / pixelRatio);
            float dx = (float)(((tx * MercatorProjection.TileSize * scaleRatio) - left) / pixelRatio);
            float dy = (float)(((ty * MercatorProjection.TileSize * scaleRatio) - top) / pixelRatio);
            return (tx, ty, placed, scale, dx, dy);
        }

        int attemptedTiles = 0;
        int unreadableTiles = 0;
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

                renderContext.RequestedTileCount++;
                TileLoad load = await this.LoadTileAsync(activeSource, tileZoom, fetchX, ty, session, renderContext.AllowIncompleteMaps, cancellationToken).ConfigureAwait(false);
                switch (load.Status)
                {
                    case TileLoadStatus.Loaded:
                        loadedTiles.Add(Place(load.Tile!, tx, ty));
                        break;
                    case TileLoadStatus.Unavailable:
                        renderContext.UnavailableTileCount++;
                        break;
                    case TileLoadStatus.Absent:
                        renderContext.AbsentTileCount++;
                        break;
                    case TileLoadStatus.Unreadable:
                        unreadableTiles++;
                        break;
                }
            }
        }

        // キャンセルされた場合は、取得元が取得失敗として返した結果より先に、キャンセルとして終わる
        cancellationToken.ThrowIfCancellationRequested();

        // 描画できるタイルが 1 枚もない場合は配信範囲外として扱う。白紙の地図を成功として返すと、
        // 国外への切替（404 のみが契機）が働かず、一括生成でも異常に気付けない。
        if (unreadableTiles > 0 || (attemptedTiles > 0 && loadedTiles.Count == 0
            && !(renderContext.AllowIncompleteMaps == true && renderContext.UnavailableTileCount > 0)))
        {
            Uri requestUri = BuildRequestUri(activeSource, tileSource, tileZoom, firstFetchX, firstFetchY);

            // 内容を受け取れたのに読めなかった場合は、配信範囲外ではなく指定の誤りである。
            // 404 にすると配信範囲外の切り替え（代替ソース）に隠れてしまうため、ステータスなしの失敗として報告する
            if (unreadableTiles > 0)
            {
                throw CreateUnreadableTileException(requestUri);
            }

            throw new TileFetchException(
                string.Create(CultureInfo.InvariantCulture, $"この地点には描画できるベクタータイルがありません（配信範囲外の可能性があります）: ズーム {tileZoom}"),
                requestUri,
                HttpStatusCode.NotFound,
                null);
        }

        // 5. SkiaSharp によるキャンバス描画
        if (parallel is not null)
        {
            await DrawParallelAsync(parallel, canvas, loadedTiles, activeStyle, styleZoom, (float)pixelRatio, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        // 背景クリア（行列に影響されないので、拡大の前でも後でも同じ）。以降は論理ピクセルで描く
        canvas.Clear(activeStyle.BackgroundColor);
        canvas.Scale((float)pixelRatio);

        var viewport = new SKRect(0, 0, (float)logicalWidth, (float)logicalHeight);
        var collisionGrid = new LabelCollisionGrid();

        // パス 1: 全タイルの面（ポリゴン）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = loadedTiles[i];
            canvas.Save();
            canvas.Translate(item.dx, item.dy);
            canvas.Scale(item.scale, item.scale);

            activeStyle.RenderPolygons(canvas, item.tile, item.scale, styleZoom, cancellationToken);

            canvas.Restore();
        }

        // パス 2: 全タイルの線（道路・鉄道・ライン）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = loadedTiles[i];
            canvas.Save();
            canvas.Translate(item.dx, item.dy);
            canvas.Scale(item.scale, item.scale);

            activeStyle.RenderLines(canvas, item.tile, item.scale, styleZoom, cancellationToken);

            canvas.Restore();
        }

        // パス 3a: 全タイルのシンボル（経路番号の盾など）。注記より優先して配置する
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = loadedTiles[i];
            SKMatrix tileToCanvasMatrix = SKMatrix.CreateTranslation(item.dx, item.dy);
            tileToCanvasMatrix = tileToCanvasMatrix.PreConcat(SKMatrix.CreateScale(item.scale, item.scale));

            activeStyle.RenderSymbols(canvas, item.tile, tileToCanvasMatrix, styleZoom, collisionGrid, viewport, cancellationToken);
        }

        // 道路名を全タイルの地名・施設名より先に配置する。
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = loadedTiles[i];
            SKMatrix matrix = SKMatrix.CreateScaleTranslation(item.scale, item.scale, item.dx, item.dy);
            activeStyle.RenderLineLabels(canvas, item.tile, matrix, styleZoom, collisionGrid, viewport, cancellationToken);
        }

        // パス 3b: 全タイルの注記・ラベル（画面座標系で直接描画）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = loadedTiles[i];
            SKMatrix tileToCanvasMatrix = SKMatrix.CreateTranslation(item.dx, item.dy);
            tileToCanvasMatrix = tileToCanvasMatrix.PreConcat(SKMatrix.CreateScale(item.scale, item.scale));

            activeStyle.RenderLabels(canvas, item.tile, tileToCanvasMatrix, styleZoom, collisionGrid, viewport, cancellationToken);
        }

        // 優先度の最も低い注記（番地など）は、全タイルの通常の注記を置いた後に置く（先に走査したタイルが、隣のタイルの注記を押しのけないように）
        for (int i = 0; i < loadedTiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = loadedTiles[i];
            SKMatrix tileToCanvasMatrix = SKMatrix.CreateTranslation(item.dx, item.dy);
            tileToCanvasMatrix = tileToCanvasMatrix.PreConcat(SKMatrix.CreateScale(item.scale, item.scale));

            activeStyle.RenderLowPriorityLabels(canvas, item.tile, tileToCanvasMatrix, styleZoom, collisionGrid, viewport, cancellationToken);
        }

        // 最後のパスの途中でキャンセルされた場合も、完成した画像として返さない
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// 1 枚のタイルを、デコード済みキャッシュ、なければ取得とデコードで得る。描画と事前デコードで共有する。
    /// キャッシュへは、デコードが済んだタイルだけを 1 枚ずつ入れる（途中の状態は入れない）。
    /// </summary>
    private async Task<TileLoad> LoadTileAsync(IVectorTileSource source, int zoom, int x, int y,
        TileFetchSession? session, bool allowIncompleteMaps, CancellationToken cancellationToken)
    {
        // 動的に版を解決する取得元（OpenFreeMap の TileJSON など）は、解決の前後で識別子が変わる。キャッシュの鍵の版と取得するタイルの版を
        // 揃えるため、解決した識別子を鍵にし、同じ識別子でタイルを取得する（CachedVectorTileSource と同じ作法。通信を止めているときは解決しない）
        string? effectiveSourceId = null;
        DecodedTileCache.TileKey cacheKey = default;
        if (this.decodedTileCache is not null)
        {
            effectiveSourceId = session?.IsCacheOnly == true
                ? source.SourceId
                : await source.GetSourceIdAsync(cancellationToken).ConfigureAwait(false);
            cacheKey = new DecodedTileCache.TileKey(effectiveSourceId, zoom, x, y);
            if (this.decodedTileCache.TryGet(cacheKey, out VectorTile? cachedTile))
            {
                return new TileLoad(TileLoadStatus.Loaded, cachedTile);
            }
        }

        byte[]? tileData;
        try
        {
            tileData = effectiveSourceId is null
                ? await source.GetTileAsync(zoom, x, y, session, cancellationToken).ConfigureAwait(false)
                : await source.GetTileForSourceIdAsync(zoom, x, y, effectiveSourceId, session, cancellationToken).ConfigureAwait(false);
        }
        catch (TileFetchException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (TileFetchException exception) when (allowIncompleteMaps
            && (exception.IsTransient || exception.IsCacheOnlyMiss) && !cancellationToken.IsCancellationRequested)
        {
            return new TileLoad(TileLoadStatus.Unavailable, null);
        }

        if (tileData is null || tileData.Length == 0)
        {
            return new TileLoad(TileLoadStatus.Absent, null);
        }

        VectorTile tile;
        bool readable;
        // デコードは同期処理なので、同じスレッドの割り当て量の差を、キャッシュの容量に数えるタイルの大きさの見積もりにする
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            readable = VectorTileDecoder.TryDecode(tileData, out tile);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            // 利用者が指定した配信元が、タイルではない内容（エラーページなど）を返した場合
            return new TileLoad(TileLoadStatus.Unreadable, null);
        }

        // レイヤーの項目を 1 つも読めない場合は、ベクタータイルとして読めていない（デコーダーは不正な内容でも例外にしない）
        if (!readable)
        {
            return new TileLoad(TileLoadStatus.Unreadable, null);
        }

        long decodedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (effectiveSourceId is not null
            && (session?.IsCacheOnly == true
                || string.Equals(effectiveSourceId, await source.GetSourceIdAsync(cancellationToken).ConfigureAwait(false), StringComparison.Ordinal)))
        {
            // 取得の間に識別子（版）が変わっていたら、どの版のタイルか分からないので保存しない
            this.decodedTileCache!.Add(cacheKey, tile, decodedBytes);
        }

        return new TileLoad(TileLoadStatus.Loaded, tile);
    }

    private static Uri BuildRequestUri(IVectorTileSource activeSource, TileSource? tileSource, int zoom, int x, int y)
        => tileSource?.BuildTileUri(zoom, x, y)
            ?? new Uri(string.Create(CultureInfo.InvariantCulture, $"vector-tile:{activeSource.SourceId}/{zoom}/{x}/{y}"), UriKind.Absolute);

    private static TileFetchException CreateUnreadableTileException(Uri requestUri)
        => new(
            "配信元の応答をベクタータイル（MVT / PBF）として読み取れません。URL・ファイルとレイヤー構成（スキーマ）の指定を確認してください。",
            requestUri,
            null,
            null);

    private enum TileLoadStatus
    {
        Loaded,
        Unavailable,
        Absent,
        Unreadable,
    }

    private readonly record struct TileLoad(TileLoadStatus Status, VectorTile? Tile);

    /// <summary>
    /// 面・線を、画像を横に分けた帯ごとに並列で描く（帯は連続した行なので、同じ画素メモリの別範囲を別のサーフェスで描ける）。
    /// 注記は衝突判定が逐次のため 1 スレッドで、並列にする場合は別の透明な画像へ描いて最後に重ね、しない場合は帯の描画のあとに本体へ直接描く。
    /// </summary>
    private static async Task DrawParallelAsync(
        ParallelDraw parallel,
        SKCanvas canvas,
        IReadOnlyList<(int tx, int ty, VectorTile tile, float scale, float dx, float dy)> tiles,
        IVectorMapStyle style,
        int styleZoom,
        float pixelRatio,
        CancellationToken cancellationToken)
    {
        SKBitmap bitmap = parallel.Bitmap;
        int width = bitmap.Width;
        int height = bitmap.Height;

        // スタイルへ渡す座標（衝突判定の範囲を含む）は論理ピクセル。画像は物理ピクセルのまま、描くキャンバスを pixelRatio 倍に拡大する
        var viewport = new SKRect(0, 0, width / pixelRatio, height / pixelRatio);
        var collisionGrid = new LabelCollisionGrid();

        void DrawLabels(SKCanvas target)
        {
            // 全タイルのシンボル → 道路名 → その他の注記の順に配置する（逐次の描画と同じ）
            foreach (var item in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                style.RenderSymbols(target, item.tile, SKMatrix.CreateScaleTranslation(item.scale, item.scale, item.dx, item.dy), styleZoom, collisionGrid, viewport, cancellationToken);
            }

            foreach (var item in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                style.RenderLineLabels(target, item.tile, SKMatrix.CreateScaleTranslation(item.scale, item.scale, item.dx, item.dy), styleZoom, collisionGrid, viewport, cancellationToken);
            }

            foreach (var item in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                style.RenderLabels(target, item.tile, SKMatrix.CreateScaleTranslation(item.scale, item.scale, item.dx, item.dy), styleZoom, collisionGrid, viewport, cancellationToken);
            }

            foreach (var item in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                style.RenderLowPriorityLabels(target, item.tile, SKMatrix.CreateScaleTranslation(item.scale, item.scale, item.dx, item.dy), styleZoom, collisionGrid, viewport, cancellationToken);
            }
        }

        SKBitmap? labelBitmap = null;
        Task? labelTask = null;
        try
        {
            if (parallel.ParallelLabels)
            {
                labelBitmap = new SKBitmap(width, height, bitmap.ColorType, SKAlphaType.Premul);
                SKBitmap labelTarget = labelBitmap;
                labelTask = Task.Run(
                    () =>
                    {
                        using var labelCanvas = new SKCanvas(labelTarget);
                        labelCanvas.Clear(SKColors.Transparent);
                        labelCanvas.Scale(pixelRatio);
                        DrawLabels(labelCanvas);
                    },
                    CancellationToken.None);
            }

            await DrawBandsAsync(bitmap, tiles, style, styleZoom, pixelRatio, Math.Min(parallel.BandCount, height), cancellationToken).ConfigureAwait(false);

            if (labelTask is null)
            {
                canvas.Save();
                canvas.Scale(pixelRatio);
                DrawLabels(canvas);
                canvas.Restore();
                return;
            }

            // 注記の画像は物理ピクセルのまま、拡大しない本体のキャンバスへ重ねる
            await labelTask.ConfigureAwait(false);
            using SKImage labelImage = SKImage.FromBitmap(labelBitmap!);
            canvas.DrawImage(labelImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        finally
        {
            // 帯の描画が失敗・キャンセルされても、注記を描いているスレッドが終わるまで、その画像を破棄しない
            if (labelTask is not null)
            {
                await Task.WhenAny(labelTask).ConfigureAwait(false);
            }

            labelBitmap?.Dispose();
        }
    }

    private static async Task DrawBandsAsync(
        SKBitmap bitmap,
        IReadOnlyList<(int tx, int ty, VectorTile tile, float scale, float dx, float dy)> tiles,
        IVectorMapStyle style,
        int styleZoom,
        float pixelRatio,
        int bandCount,
        CancellationToken cancellationToken)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        IntPtr pixels = bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes;
        SKColor background = style.BackgroundColor;
        var options = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = bandCount };

        void DrawBand(int band)
        {
            int top = height * band / bandCount;
            int bottom = height * (band + 1) / bandCount;
            if (bottom <= top)
            {
                return;
            }

            var info = new SKImageInfo(width, bottom - top, bitmap.ColorType, bitmap.AlphaType);
            using SKSurface surface = SKSurface.Create(info, pixels + (top * rowBytes), rowBytes)
                ?? throw new InvalidOperationException("帯の描画先を作れませんでした。");
            SKCanvas bandCanvas = surface.Canvas;
            bandCanvas.Clear(background);

            // 帯の位置は物理ピクセルの行。先に帯の位置へ移動してから、論理ピクセルへ拡大する
            bandCanvas.Translate(0, -top);
            bandCanvas.Scale(pixelRatio);

            // 面 → 線の順に、全タイルを描く（逐次の描画と同じ）
            foreach (var item in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bandCanvas.Save();
                bandCanvas.Translate(item.dx, item.dy);
                bandCanvas.Scale(item.scale, item.scale);
                style.RenderPolygons(bandCanvas, item.tile, item.scale, styleZoom, cancellationToken);
                bandCanvas.Restore();
            }

            foreach (var item in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bandCanvas.Save();
                bandCanvas.Translate(item.dx, item.dy);
                bandCanvas.Scale(item.scale, item.scale);
                style.RenderLines(bandCanvas, item.tile, item.scale, styleZoom, cancellationToken);
                bandCanvas.Restore();
            }
        }

        try
        {
            await Task.Run(() => Parallel.For(0, bandCount, options, DrawBand), cancellationToken).ConfigureAwait(false);
        }
        catch (AggregateException exception)
        {
            // 並列の描画の失敗は AggregateException に包まれる（複数の帯が同じ理由で同時に失敗し得る）。
            // 利用者には元の例外（スタイルの失敗など）として見せるため、最初の失敗を投げ直す
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
        }
    }

    private sealed record ParallelDraw(SKBitmap Bitmap, int BandCount, bool ParallelLabels);
}
