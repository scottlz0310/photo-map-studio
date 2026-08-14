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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.SupportsZoom(zoom))
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoom),
                zoom,
                $"{source.Name} が対応するズームの範囲外です（{source.MinZoom}〜{source.MaxZoom}）。");
        }

        string key = TileCacheKey.Create(source.UrlTemplate, zoom, x, y);

        byte[]? cached = await this.cache.TryReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        byte[] content = await this.client.GetTileAsync(source, zoom, x, y, cancellationToken).ConfigureAwait(false);
        await this.cache.WriteAsync(key, content, cancellationToken).ConfigureAwait(false);
        return content;
    }
}
