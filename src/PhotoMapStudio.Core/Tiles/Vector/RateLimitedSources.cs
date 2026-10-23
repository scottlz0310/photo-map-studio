using PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 他者の配信元へのネットワーク要求の間隔を空ける <see cref="IVectorTileSource"/>。
/// キャッシュの内側に置き、キャッシュ命中は待たせない。
/// </summary>
internal sealed class RateLimitedVectorTileSource(IVectorTileSource inner, RequestIntervalGate gate) : IVectorTileSource, IDisposable
{
    public string SourceId => inner.SourceId;

    public int MaxZoom => inner.MaxZoom;

    public Task<string> GetSourceIdAsync(CancellationToken cancellationToken = default)
        => inner.GetSourceIdAsync(cancellationToken);

    public async Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
    {
        using IDisposable lease = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await inner.GetTileAsync(z, x, y, session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetTileForSourceIdAsync(
        int z,
        int x,
        int y,
        string resolvedSourceId,
        TileFetchSession? session,
        CancellationToken cancellationToken = default)
    {
        using IDisposable lease = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await inner.GetTileForSourceIdAsync(z, x, y, resolvedSourceId, session, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        (inner as IDisposable)?.Dispose();
        gate.Dispose();
    }
}

/// <summary>
/// 他者の配信元への Range 要求の間隔を空ける <see cref="IPmtilesByteSource"/>。
/// </summary>
internal sealed class RateLimitedPmtilesByteSource(IPmtilesByteSource inner, RequestIntervalGate gate) : IPmtilesByteSource
{
    public async Task<byte[]> ReadRangeAsync(long offset, int length, TileFetchSession? session, CancellationToken cancellationToken)
    {
        using IDisposable lease = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await inner.ReadRangeAsync(offset, length, session, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        inner.Dispose();
        gate.Dispose();
    }
}
