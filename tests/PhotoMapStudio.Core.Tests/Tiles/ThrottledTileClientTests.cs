using System.Diagnostics;

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

    [Fact]
    public async Task 最小間隔を空けて発行する()
    {
        var source = new TileSource(
            "テスト",
            "https://tile.example.com/{z}/{x}/{y}.png",
            0,
            19,
            "出典",
            new TileRateLimit(1, TimeSpan.FromMilliseconds(100)));
        var inner = new ConcurrencyTrackingClient(TimeSpan.Zero);
        using var client = new ThrottledTileClient(inner);

        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 3; i++)
        {
            await client.GetTileAsync(source, 15, i, 0, CancellationToken.None);
        }

        // 1 回目は待たないため、待機は 2 回分
        Assert.True(Stopwatch.GetElapsedTime(start) >= TimeSpan.FromMilliseconds(200));
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
