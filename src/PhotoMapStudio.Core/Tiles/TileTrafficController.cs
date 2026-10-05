namespace PhotoMapStudio.Core.Tiles;

/// <summary>同じ配信ホストへのプレビューと一括生成に共有する制限。</summary>
public sealed class TileTrafficController
{
    private readonly Dictionary<string, int> batches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object synchronization = new();

    /// <summary>一括生成の実行中、同じホストへの対話取得も一括の間隔に揃える。</summary>
    public IDisposable BeginBatch(TileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string host = source.BuildTileUri(0, 0, 0).Host;
        lock (this.synchronization)
        {
            this.batches.TryGetValue(host, out int count);
            this.batches[host] = count + 1;
        }

        return new BatchLease(this, host);
    }

    /// <summary>要求に適用する最小間隔。</summary>
    public TimeSpan GetMinimumInterval(TileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (this.synchronization)
        {
            return this.batches.ContainsKey(source.BuildTileUri(0, 0, 0).Host)
                ? source.BatchRateLimit.MinimumInterval : source.RateLimit.MinimumInterval;
        }
    }

    /// <summary>一括生成中は同じホストに単一接続を適用する。</summary>
    public int GetConcurrencyLimit(TileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (this.synchronization)
        {
            return this.batches.ContainsKey(source.BuildTileUri(0, 0, 0).Host)
                ? source.BatchRateLimit.MaxConcurrentRequests : source.RateLimit.MaxConcurrentRequests;
        }
    }

    private sealed class BatchLease(TileTrafficController owner, string host) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            lock (owner.synchronization)
            {
                if (this.disposed)
                {
                    return;
                }

                this.disposed = true;
                if (--owner.batches[host] == 0)
                {
                    owner.batches.Remove(host);
                }
            }
        }
    }
}
