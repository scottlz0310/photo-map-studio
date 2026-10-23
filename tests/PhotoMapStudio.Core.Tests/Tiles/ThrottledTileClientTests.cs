using System.Diagnostics;

using PhotoMapStudio.Core.Tests.TestSupport;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Tiles;

public class ThrottledTileClientTests
{
    [Fact]
    public async Task 一括中は対話要求も同じ単一接続と一秒間隔で送る()
    {
        var source = new TileSource("カスタム", "https://example.com/{z}/{x}/{y}.png", 0, 19, "出典", new TileRateLimit(2, TimeSpan.Zero));
        var traffic = new TileTrafficController();
        var inner = new ConcurrencyTrackingClient(TimeSpan.FromMilliseconds(20));
        using var client = new ThrottledTileClient(inner, trafficController: traffic);
        using IDisposable lease = traffic.BeginBatch(source);
        long started = Stopwatch.GetTimestamp();
        await Task.WhenAll(Enumerable.Range(0, 3).Select(index => client.GetTileAsync(source, 15, index, 0, CancellationToken.None)));
        Assert.Equal(1, inner.MaxObservedConcurrency);
        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task 同時実行数を方針の上限に抑える()
    {
        var source = new TileSource(
            "テスト",
            "https://tile.example.com/{z}/{x}/{y}.png",
            0,
            19,
            "出典",
            new TileRateLimit(2, TimeSpan.Zero));
        var inner = new ConcurrencyTrackingClient(TimeSpan.FromMilliseconds(30));
        using var client = new ThrottledTileClient(inner);

        await Task.WhenAll(Enumerable
            .Range(0, 8)
            .Select(i => client.GetTileAsync(source, 15, i, 0, CancellationToken.None)));

        Assert.Equal(2, inner.MaxObservedConcurrency);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(1000)]
    public async Task タイマーが早く通知しても最小間隔を空けて発行する(int milliseconds)
    {
        var clock = new EarlyTimerTimeProvider();
        TimeSpan interval = TimeSpan.FromMilliseconds(milliseconds);
        var source = new TileSource(
            "テスト",
            "https://tile.example.com/{z}/{x}/{y}.png",
            0,
            19,
            "出典",
            new TileRateLimit(1, interval));
        var inner = new ConcurrencyTrackingClient(TimeSpan.Zero);
        using var client = new ThrottledTileClient(inner, clock);

        await client.GetTileAsync(source, 15, 0, 0, CancellationToken.None);
        Task<byte[]> pending = client.GetTileAsync(source, 15, 1, 0, CancellationToken.None);
        Assert.Equal(interval, await clock.NextDelayAsync());
        Assert.False(pending.IsCompleted);
        clock.Advance(interval / 2);
        Task<TimeSpan> remaining = clock.NextDelayAsync();
        await Task.WhenAny(pending, remaining).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        Assert.Equal(TimeSpan.FromMilliseconds(Math.Ceiling(interval.TotalMilliseconds / 2)), await remaining.ConfigureAwait(true));
        clock.Advance(interval / 2);
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task 残時間待機のキャンセル後も次の要求を発行できる()
    {
        var clock = new EarlyTimerTimeProvider();
        var interval = TimeSpan.FromMilliseconds(100);
        var source = new TileSource("テスト", "https://example.test/{z}/{x}/{y}.png", 0, 19, "出典", new TileRateLimit(1, interval));
        using var client = new ThrottledTileClient(new ConcurrencyTrackingClient(TimeSpan.Zero), clock);
        await client.GetTileAsync(source, 15, 0, 0, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task<byte[]> pending = client.GetTileAsync(source, 15, 1, 0, cancellation.Token);
        await clock.NextDelayAsync();
        clock.Advance(interval / 2);
        await clock.NextDelayAsync();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Task<byte[]> next = client.GetTileAsync(source, 15, 2, 0, CancellationToken.None);
        await clock.NextDelayAsync();
        Assert.False(next.IsCompleted);
        clock.Advance(interval);
        await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task タイルソースごとに制御は独立する()
    {
        var fast = new TileSource(
            "高速",
            "https://a.example.com/{z}/{x}/{y}.png",
            0,
            19,
            "出典",
            new TileRateLimit(1, TimeSpan.Zero));
        var slow = new TileSource(
            "低速",
            "https://b.example.com/{z}/{x}/{y}.png",
            0,
            19,
            "出典",
            new TileRateLimit(1, TimeSpan.FromMilliseconds(500)));
        var inner = new ConcurrencyTrackingClient(TimeSpan.Zero);
        using var client = new ThrottledTileClient(inner);

        await client.GetTileAsync(slow, 15, 0, 0, CancellationToken.None);
        long start = Stopwatch.GetTimestamp();
        await client.GetTileAsync(fast, 15, 0, 0, CancellationToken.None);

        Assert.True(Stopwatch.GetElapsedTime(start) < TimeSpan.FromMilliseconds(500));
    }

    private sealed class ConcurrencyTrackingClient(TimeSpan duration) : ITileClient
    {
        private readonly Lock gate = new();
        private int current;

        public int MaxObservedConcurrency { get; private set; }

        public async Task<byte[]> GetTileAsync(
            TileSource source,
            int zoom,
            int x,
            int y,
            CancellationToken cancellationToken, TileFetchSession? session = null)
        {
            lock (this.gate)
            {
                this.current++;
                this.MaxObservedConcurrency = Math.Max(this.MaxObservedConcurrency, this.current);
            }

            try
            {
                if (duration > TimeSpan.Zero)
                {
                    await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
                }

                return [0x01];
            }
            finally
            {
                lock (this.gate)
                {
                    this.current--;
                }
            }
        }
    }
}
