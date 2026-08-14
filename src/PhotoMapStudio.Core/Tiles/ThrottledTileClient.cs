using System.Collections.Concurrent;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイルソースごとのレート制御方針（<see cref="TileRateLimit"/>）を適用する <see cref="ITileClient"/> のデコレーター。
/// </summary>
public sealed class ThrottledTileClient : ITileClient, IDisposable
{
    private readonly ITileClient inner;
    private readonly TimeProvider timeProvider;
    private readonly ConcurrentDictionary<string, Throttle> throttles = new(StringComparer.Ordinal);

    /// <summary>
    /// デコレーターを初期化する。
    /// </summary>
    /// <param name="inner">実際にタイルを取得するクライアント。</param>
    /// <param name="timeProvider">間隔制御に使う時刻源。</param>
    public ThrottledTileClient(ITileClient inner, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
        this.timeProvider = timeProvider ?? TimeProvider.System;
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

        Throttle throttle = this.throttles.GetOrAdd(
            source.UrlTemplate,
            static (_, rateLimit) => new Throttle(rateLimit),
            source.RateLimit);

        await throttle.Concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await throttle.WaitForIntervalAsync(this.timeProvider, cancellationToken).ConfigureAwait(false);
            return await this.inner.GetTileAsync(source, zoom, x, y, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            throttle.Concurrency.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (Throttle throttle in this.throttles.Values)
        {
            throttle.Dispose();
        }

        this.throttles.Clear();
    }

    private sealed class Throttle : IDisposable
    {
        private readonly TimeSpan minimumInterval;
        private readonly SemaphoreSlim intervalGate = new(1, 1);
        private DateTimeOffset lastRequestedAt = DateTimeOffset.MinValue;

        public Throttle(TileRateLimit rateLimit)
        {
            this.minimumInterval = rateLimit.MinimumInterval;
            this.Concurrency = new SemaphoreSlim(rateLimit.MaxConcurrentRequests, rateLimit.MaxConcurrentRequests);
        }

        public SemaphoreSlim Concurrency { get; }

        public async Task WaitForIntervalAsync(TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            if (this.minimumInterval <= TimeSpan.Zero)
            {
                return;
            }

            // 直前の発行時刻の更新までを直列化し、間隔が詰まらないようにする
            await this.intervalGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TimeSpan elapsed = timeProvider.GetUtcNow() - this.lastRequestedAt;
                if (elapsed < this.minimumInterval)
                {
                    await Task.Delay(this.minimumInterval - elapsed, timeProvider, cancellationToken).ConfigureAwait(false);
                }

                this.lastRequestedAt = timeProvider.GetUtcNow();
            }
            finally
            {
                this.intervalGate.Release();
            }
        }

        public void Dispose()
        {
            this.Concurrency.Dispose();
            this.intervalGate.Dispose();
        }
    }
}
