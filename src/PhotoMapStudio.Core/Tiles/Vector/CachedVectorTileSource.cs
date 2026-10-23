namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// <see cref="ITileCache"/> を用いてベクタータイルを透過的にキャッシュするデコレータ。
/// </summary>
public sealed class CachedVectorTileSource : IVectorTileSource
{
    private readonly IVectorTileSource inner;
    private readonly ITileCache cache;

    /// <summary>
    /// キャッシュ付きベクタータイルソースを初期化する。
    /// </summary>
    /// <param name="inner">実際の取得を行う元のベクタータイルソース。</param>
    /// <param name="cache">タイルキャッシュ。</param>
    public CachedVectorTileSource(IVectorTileSource inner, ITileCache cache)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public string SourceId => this.inner.SourceId;

    /// <inheritdoc />
    public int MaxZoom => this.inner.MaxZoom;

    /// <inheritdoc />
    public Task PrepareAsync(TileFetchSession? session, CancellationToken cancellationToken = default)
        => this.inner.PrepareAsync(session, cancellationToken);

    /// <inheritdoc />
    public Task<string> GetSourceIdAsync(CancellationToken cancellationToken = default)
        => this.inner.GetSourceIdAsync(cancellationToken);

    /// <inheritdoc />
    public Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken cancellationToken = default)
        => this.GetTileAsync(z, x, y, session: null, cancellationToken);

    /// <inheritdoc />
    public async Task<byte[]?> GetTileAsync(
        int z,
        int x,
        int y,
        TileFetchSession? session,
        CancellationToken cancellationToken = default)
    {
        session?.ThrowIfStopped();

        string sourceId = await this.inner.GetSourceIdAsync(cancellationToken).ConfigureAwait(false);
        string key = VectorTileCacheKey.Create(sourceId, z, x, y);

        byte[]? cached = await this.cache.TryReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null && cached.Length > 0)
        {
            session?.RecordSuccess(cached: true);
            return cached;
        }

        byte[]? fetched;
        try
        {
            fetched = await this.inner.GetTileForSourceIdAsync(z, x, y, sourceId, session, cancellationToken).ConfigureAwait(false);
            session?.RecordSuccess(cached: false);
        }
        catch (TileFetchException ex)
        {
            session?.RecordFailure(ex);
            session?.ThrowIfStopped();
            throw;
        }

        if (fetched is not null && fetched.Length > 0)
        {
            try
            {
                await this.cache.WriteAsync(key, fetched, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // キャッシュ書き込みに失敗しても、取得したデータ自体はそのまま呼び出し元へ返す
            }
        }

        return fetched;
    }
}
