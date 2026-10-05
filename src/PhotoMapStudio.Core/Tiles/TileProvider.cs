namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// キャッシュとタイル取得を組み合わせた <see cref="ITileProvider"/> の実装。
/// </summary>
public sealed class TileProvider : ITileProvider
{
    private readonly ITileClient client;
    private readonly ITileCache cache;

    /// <summary>
    /// プロバイダーを初期化する。
    /// </summary>
    /// <param name="client">タイル取得クライアント。</param>
    /// <param name="cache">タイルキャッシュ。</param>
    public TileProvider(ITileClient client, ITileCache cache)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(cache);
        this.client = client;
        this.cache = cache;
    }

    /// <inheritdoc />
    public async Task<byte[]> GetTileAsync(
        TileSource source,
        int zoom,
        int x,
        int y,
        CancellationToken cancellationToken, TileFetchSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.SupportsZoom(zoom))
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoom),
                zoom,
                $"{source.Name} が対応するズームの範囲外です（{source.MinZoom}〜{source.MaxZoom}）。");
        }

        session?.ThrowIfStopped();
        string key = TileCacheKey.Create(source.UrlTemplate, zoom, x, y);

        byte[]? cached = await this.cache.TryReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            session?.RecordSuccess(cached: true);
            return cached;
        }

        if (session is not null && source.IsOfficialOpenStreetMap)
        {
            throw new InvalidOperationException("OSM公式サーバーの一括新規取得は利用できません。一括取得・画像保存が許可されたOSM系配信元をカスタム設定で指定してください。");
        }

        byte[] content;
        try
        {
            content = await this.client.GetTileAsync(source, zoom, x, y, cancellationToken, session).ConfigureAwait(false);
            session?.RecordSuccess(cached: false);
        }
        catch (TileFetchException exception)
        {
            session?.RecordFailure(exception);
            session?.ThrowIfStopped();
            throw;
        }
        await this.cache.WriteAsync(key, content, cancellationToken).ConfigureAwait(false);
        return content;
    }
}
