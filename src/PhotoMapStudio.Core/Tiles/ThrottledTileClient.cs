using System.Collections.Concurrent;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイルソースごとのレート制御方針（<see cref="TileRateLimit"/>）を適用する <see cref="ITileClient"/> のデコレーター。
/// </summary>
public sealed class ThrottledTileClient : ITileClient, IDisposable
{
    private readonly ITileClient inner;
    private readonly TimeProvider timeProvider;
    private readonly TileTrafficController trafficController;
    private readonly ConcurrentDictionary<string, Throttle> throttles = new(StringComparer.Ordinal);

    /// <summary>
    /// デコレーターを初期化する。
    /// </summary>
    /// <param name="inner">実際にタイルを取得するクライアント。</param>
    /// <param name="timeProvider">間隔制御に使う時刻源。</param>
    public ThrottledTileClient(ITileClient inner, TimeProvider? timeProvider = null, TileTrafficController? trafficController = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
        this.trafficController = trafficController ?? new TileTrafficController();
        this.timeProvider = timeProvider ?? TimeProvider.System;
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

        Throttle throttle = this.throttles.GetOrAdd(
            source.BuildTileUri(0, 0, 0).Host,
            static _ => new Throttle());

        await throttle.EnterAsync(() => this.trafficController.GetConcurrencyLimit(source), cancellationToken).ConfigureAwait(false);
        try
        {
            await throttle.WaitForIntervalAsync(this.timeProvider, this.trafficController.GetMinimumInterval(source), cancellationToken).ConfigureAwait(false);
            return await this.inner.GetTileAsync(source, zoom, x, y, cancellationToken, session).ConfigureAwait(false);
        }
        finally
        {
            throttle.Exit();
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
        private readonly SemaphoreSlim intervalGate = new(1, 1);
        private DateTimeOffset lastRequestedAt = DateTimeOffset.MinValue;

        private readonly object synchronization = new();
        private int activeCount;
        private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task EnterAsync(Func<int> limit, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task wait;
                lock (this.synchronization)
                {
                    if (this.activeCount < limit())
                    {
                        this.activeCount++;
                        return;
                    }
                    wait = this.changed.Task;
                }
                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public void Exit()
        {
            lock (this.synchronization)
            {
                this.activeCount--;
                this.changed.TrySetResult();
                this.changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public async Task WaitForIntervalAsync(TimeProvider timeProvider, TimeSpan minimumInterval, CancellationToken cancellationToken)
        {
            if (minimumInterval <= TimeSpan.Zero)
            {
                return;
            }

            // 直前の発行時刻の更新までを直列化し、間隔が詰まらないようにする
            await this.intervalGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TimeSpan elapsed = timeProvider.GetUtcNow() - this.lastRequestedAt;
                if (elapsed < minimumInterval)
                {
                    await Task.Delay(minimumInterval - elapsed, timeProvider, cancellationToken).ConfigureAwait(false);
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
            this.intervalGate.Dispose();
        }
    }
}
