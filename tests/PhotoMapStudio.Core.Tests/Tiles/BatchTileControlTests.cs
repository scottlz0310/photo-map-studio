using System.Net;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.Core.Tests.Tiles;

public class BatchTileControlTests
{
    private static TileSource Source => TileSources.GsiPale;

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task 十回連続の失敗で停止し以降のHTTP要求を送らない(int status)
    {
        using var factory = new StubHttpClientFactory(_ => status == 0 ? throw new HttpRequestException("切断")
            : status == -1 ? throw new TaskCanceledException("タイムアウト") : new HttpResponseMessage((HttpStatusCode)status));
        var provider = new TileProvider(new HttpTileClient(factory), new MemoryCache());
        var session = new TileFetchSession(TimeProvider.System);
        for (int index = 0; index < TileFetchSession.FailureLimit - 1; index++)
        {
            await Assert.ThrowsAsync<TileFetchException>(() => provider.GetTileAsync(Source, 15, index, 0, CancellationToken.None, session));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTileAsync(Source, 15, 10, 0, CancellationToken.None, session));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTileAsync(Source, 15, 11, 0, CancellationToken.None, session));
        Assert.Equal(TileFetchSession.FailureLimit, factory.Requests.Count);
        Assert.Equal(factory.Requests.Count, session.Snapshot().NetworkCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 配信元の成功だけで連続数をリセットし404は失敗数に含めない(bool cached)
    {
        var session = new TileFetchSession(TimeProvider.System);
        var failure = new TileFetchException("503", new Uri("https://example.com"), HttpStatusCode.ServiceUnavailable, null);
        for (int index = 0; index < 9; index++) { session.RecordFailure(failure); }
        session.RecordFailure(new("404", new Uri("https://example.com"), HttpStatusCode.NotFound, null));
        Assert.False(session.IsStopped);
        session.RecordSuccess(cached);
        if (cached)
        {
            session.RecordFailure(failure);
            Assert.True(session.IsStopped);
            return;
        }
        for (int index = 0; index < 9; index++) { session.RecordFailure(failure); }
        Assert.False(session.IsStopped);
        session.RecordFailure(failure); Assert.True(session.IsStopped);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task 再実行待機のヘッダーを原因とともに伝え自動再試行しない(int status)
    {
        using var factory = new StubHttpClientFactory(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(60)); return response;
        });
        TileFetchException error = await Assert.ThrowsAsync<TileFetchException>(() => new HttpTileClient(factory).GetTileAsync(Source, 15, 0, 0, CancellationToken.None));
        Assert.Equal("60", error.RetryAfter);
        Assert.Contains("Retry-After: 60", error.Message, StringComparison.Ordinal);
        Assert.Contains(Source.BuildTileUri(15, 0, 0).Host, error.Message, StringComparison.Ordinal);
        Assert.Single(factory.Requests);
    }

    [Theory]
    [InlineData("https://tile.openstreetmap.org/{z}/{x}/{y}.png", true)]
    [InlineData("https://a.tile.openstreetmap.org/{z}/{x}/{y}.png", true)]
    [InlineData("https://tile.openstreetmap.org./{z}/{x}/{y}.png", true)]
    [InlineData("https://allowed.example.com/{z}/{x}/{y}.png", false)]
    public async Task カスタムでも公式OSMは一括新規取得せずプレビューは利用できる(string template, bool official)
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, [1]);
        var source = new TileSource("カスタム", template, 0, 19, "出典", TileRateLimit.Conservative);
        var provider = new TileProvider(new HttpTileClient(factory), new MemoryCache());
        var session = new TileFetchSession(TimeProvider.System);
        Assert.Equal(official, source.IsOfficialOpenStreetMap);
        if (official)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTileAsync(source, 15, 0, 0, CancellationToken.None, session));
            Assert.Empty(factory.Requests);
        }
        else { await provider.GetTileAsync(source, 15, 0, 0, CancellationToken.None, session); }
        await provider.GetTileAsync(source, 15, 1, 0, CancellationToken.None);
        Assert.Equal(official ? 1 : 2, factory.Requests.Count);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    public async Task キャッシュ命中は取得数と失敗数を増やさず減速しない(int hits)
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, [1]);
        var provider = new TileProvider(new HttpTileClient(factory), new MemoryCache());
        var clock = new FixedTimeProvider(DateTimeOffset.UnixEpoch);
        var session = new TileFetchSession(clock);
        await provider.GetTileAsync(Source, 15, 0, 0, CancellationToken.None, session);
        for (int index = 0; index < hits; index++) { await provider.GetTileAsync(Source, 15, 0, 0, CancellationToken.None, session); }
        clock.Advance(TimeSpan.FromSeconds(2));
        TileFetchStatistics statistics = session.Snapshot();
        Assert.Equal(1, statistics.NetworkCount); Assert.Equal(hits, statistics.CacheCount);
        Assert.Equal(0.5, statistics.AverageRate); Assert.Equal(1, statistics.MaximumRate);
        Assert.Single(factory.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 一括中は同じホストのプレビューにも固定間隔を適用して終了時に戻す(bool gsi)
    {
        TileSource source = gsi ? Source : new("カスタム", "https://example.com/{z}/{x}/{y}.png", 0, 19, "出典", TileRateLimit.Conservative);
        var traffic = new TileTrafficController();
        TimeSpan interactive = traffic.GetMinimumInterval(source);
        using (traffic.BeginBatch(source))
        {
            Assert.Equal(gsi ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(1), traffic.GetMinimumInterval(source));
        }
        Assert.Equal(interactive, traffic.GetMinimumInterval(source));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8765/{z}/{x}/{y}.png", true)]
    [InlineData("http://localhost/{z}/{x}/{y}.png", true)]
    [InlineData("http://192.168.1.10/{z}/{x}/{y}.png", false)]
    public void 一括中もループバックだけは通常の間隔と並列数を維持する(string template, bool loopback)
    {
        var source = TileSources.Custom(template, "出典");
        var traffic = new TileTrafficController();
        TimeSpan interval = traffic.GetMinimumInterval(source);
        int concurrency = traffic.GetConcurrencyLimit(source);
        using (traffic.BeginBatch(source))
        {
            Assert.Equal(loopback, traffic.GetMinimumInterval(source) == interval);
            Assert.Equal(loopback, traffic.GetConcurrencyLimit(source) == concurrency);
            if (loopback)
            {
                Assert.Equal(TimeSpan.FromMilliseconds(5), traffic.GetMinimumInterval(source));
                Assert.Equal(8, traffic.GetConcurrencyLimit(source));
            }
            else
            {
                Assert.Equal(TimeSpan.FromSeconds(1), traffic.GetMinimumInterval(source));
                Assert.Equal(1, traffic.GetConcurrencyLimit(source));
            }
        }
    }

    private sealed class MemoryCache : ITileCache
    {
        private readonly Dictionary<string, byte[]> entries = [];
        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(this.entries.GetValueOrDefault(key));
        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
        { this.entries[key] = content; return Task.CompletedTask; }
    }
}
