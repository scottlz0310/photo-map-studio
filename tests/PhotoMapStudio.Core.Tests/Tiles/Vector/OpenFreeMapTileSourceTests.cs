using System.Net;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class OpenFreeMapTileSourceTests
{
    private const string SampleTileJson = """
        {
            "tilejson": "3.0.0",
            "tiles": [
                "https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf"
            ]
        }
        """;

    [Fact]
    public async Task 明示的なテンプレート指定時はTileJSONを要求せず指定URLへ要求する()
    {
        byte[] expected = [0x1F, 0x8B, 0x08, 0x00];
        const string explicitTemplate = "https://custom.tiles.example.com/{z}/{x}/{y}.pbf";
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, expected);
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplate: explicitTemplate);

        byte[]? actual = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Single(factory.Requests);
        Assert.Equal(
            new Uri("https://custom.tiles.example.com/14/14675/5991.pbf"),
            factory.Requests[0].RequestUri);
        Assert.Equal($"openfreemap:{explicitTemplate}", source.SourceId);
    }

    [Fact]
    public async Task 既定構成ではTileJSONから最新URLテンプレートを解決してタイルを要求する()
    {
        byte[] expectedTile = [0x11, 0x22, 0x33];
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri == new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleTileJson),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedTile),
            };
        });

        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client);

        byte[]? actual = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(expectedTile, actual);
        Assert.Equal(2, factory.Requests.Count);

        // 1件目は TileJSON 要求
        Assert.Equal(new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl), factory.Requests[0].RequestUri);
        Assert.Equal(HttpMethod.Get, factory.Requests[0].Method);

        // 2件目は解決された URL テンプレートでのタイル要求
        Assert.Equal(
            new Uri("https://tiles.openfreemap.org/planet/20261020_080001_pt/14/14675/5991.pbf"),
            factory.Requests[1].RequestUri);

        // SourceId が解決された実効テンプレートに更新されていること
        Assert.Equal(
            "openfreemap:https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf",
            source.SourceId);
    }

    [Fact]
    public async Task TileJSON取得失敗時はFallbackUrlTemplateへフォールバックする()
    {
        byte[] expectedTile = [0xAA, 0xBB];
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri == new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedTile),
            };
        });

        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client);

        byte[]? actual = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(expectedTile, actual);
        Assert.Equal(2, factory.Requests.Count);
        Assert.Equal(new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl), factory.Requests[0].RequestUri);
        Assert.Equal(
            new Uri("https://tiles.openfreemap.org/planet/20261018_080001_pt/14/14675/5991.pbf"),
            factory.Requests[1].RequestUri);
        Assert.Equal($"openfreemap:{OpenFreeMapTileSource.FallbackUrlTemplate}", source.SourceId);
    }

    [Fact]
    public async Task 固有のUserAgentを表明する()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, [1]);
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplate: "https://tiles.example.com/{z}/{x}/{y}.pbf");

        await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(UserAgentProvider.Value, factory.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task 存在しないタイル404の場合はnullを返す()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.NotFound);
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplate: "https://tiles.example.com/{z}/{x}/{y}.pbf");

        byte[]? result = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task タイル要求のサーバーエラー応答時はTileFetchExceptionをスローする(HttpStatusCode status)
    {
        using var factory = StubHttpClientFactory.WithStatus(status);
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplate: "https://tiles.example.com/{z}/{x}/{y}.pbf");

        TileFetchException ex = await Assert.ThrowsAsync<TileFetchException>(
            () => source.GetTileAsync(14, 14675, 5991, CancellationToken.None));

        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public void URLテンプレートが異なればSourceIdも異なる()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK);
        using var client = factory.CreateClient("test");
        using var sourceA = new OpenFreeMapTileSource(client, urlTemplate: "https://server-a/{z}/{x}/{y}.pbf");
        using var sourceB = new OpenFreeMapTileSource(client, urlTemplate: "https://server-b/{z}/{x}/{y}.pbf");

        Assert.NotEqual(sourceA.SourceId, sourceB.SourceId);
        Assert.Equal("openfreemap:https://server-a/{z}/{x}/{y}.pbf", sourceA.SourceId);
        Assert.Equal("openfreemap:https://server-b/{z}/{x}/{y}.pbf", sourceB.SourceId);
        Assert.Equal(14, sourceA.MaxZoom);
    }

    [Fact]
    public async Task TileJSON取得に失敗してもフォールバックを確定せず待機後に再取得して最新の版へ戻る()
    {
        bool tileJsonAvailable = false;
        int tileJsonRequestCount = 0;
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri == new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl))
            {
                tileJsonRequestCount++;
                return tileJsonAvailable
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleTileJson) }
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
        });
        var time = new ManualTimeProvider();
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, timeProvider: time);

        // 1 回目: 失敗してフォールバックを使う（確定しない）
        Assert.Equal(OpenFreeMapTileSource.FallbackUrlTemplate, await source.ResolveUrlTemplateAsync(CancellationToken.None));

        // 待機中は TileJSON を再要求しない（タイルごとの再要求を避ける）
        tileJsonAvailable = true;
        Assert.Equal(OpenFreeMapTileSource.FallbackUrlTemplate, await source.ResolveUrlTemplateAsync(CancellationToken.None));
        Assert.Equal(1, tileJsonRequestCount);

        // 待機後は再取得して最新の版へ戻る
        time.Advance(OpenFreeMapTileSource.ResolveRetryInterval + TimeSpan.FromSeconds(1));
        string resolved = await source.ResolveUrlTemplateAsync(CancellationToken.None);

        Assert.Equal("https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf", resolved);
        Assert.Equal(2, tileJsonRequestCount);
    }

    [Fact]
    public async Task 解決した版を保存し再起動後にTileJSONが取得できなくても前回の版を使う()
    {
        var store = new MemoryTileCache();

        // 1 回目の起動: TileJSON を取得でき、前回の版として保存される
        using (var okFactory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleTileJson) }))
        using (var client = okFactory.CreateClient("test"))
        using (var source = new OpenFreeMapTileSource(client, urlTemplateStore: store))
        {
            await source.ResolveUrlTemplateAsync(CancellationToken.None);
        }

        // 2 回目の起動: オフライン相当。保存された版を使うので、その版のキャッシュ鍵が変わらない
        using var offlineFactory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var offlineClient = offlineFactory.CreateClient("test");
        using var offline = new OpenFreeMapTileSource(offlineClient, urlTemplateStore: store);

        string sourceId = await offline.GetSourceIdAsync(CancellationToken.None);

        Assert.Equal("openfreemap:https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf", sourceId);
    }

    [Theory]
    [InlineData("http://example.com/{z}/{x}/{y}.pbf")]
    [InlineData("https://example.com/no-placeholders.pbf")]
    [InlineData("")]
    public async Task 保存された版が不正な値ならフォールバックを使う(string stored)
    {
        var store = new MemoryTileCache();
        await store.WriteAsync(OpenFreeMapTileSource.UrlTemplateStoreKey, System.Text.Encoding.UTF8.GetBytes(stored), CancellationToken.None);
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplateStore: store);

        string sourceId = await source.GetSourceIdAsync(CancellationToken.None);

        Assert.Equal($"openfreemap:{OpenFreeMapTileSource.FallbackUrlTemplate}", sourceId);
    }

    [Fact]
    public async Task 保存値の読み込みが呼び出し元のキャンセルで中断されても次回は保存値を読み直す()
    {
        var store = new MemoryTileCache();
        await store.WriteAsync(
            OpenFreeMapTileSource.UrlTemplateStoreKey,
            System.Text.Encoding.UTF8.GetBytes("https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf"),
            CancellationToken.None);
        var cancelOnce = new CancelFirstReadTileCache(store);
        using var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplateStore: cancelOnce);

        // 1 回目: 保存値の読み込みが呼び出し元のキャンセルで中断される
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ResolveUrlTemplateAsync(CancellationToken.None));

        // 2 回目: ロード済みと誤認せず保存値を読み直し、TileJSON が取得できなくても前回の版を使う
        string sourceId = await source.GetSourceIdAsync(CancellationToken.None);

        Assert.Equal("openfreemap:https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf", sourceId);
    }

    [Fact]
    public async Task 再取得の待機期限をまたいでも取得したタイルを識別子と同じ版の鍵で保存する()
    {
        bool tileJsonAvailable = false;
        var requestedTiles = new List<Uri>();
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri == new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl))
            {
                return tileJsonAvailable
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleTileJson) }
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            requestedTiles.Add(req.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9]) };
        });
        var time = new ManualTimeProvider();
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, timeProvider: time);

        // TileJSON の取得に失敗し、待機中の状態にする（フォールバックの版を使う）
        await source.ResolveUrlTemplateAsync(CancellationToken.None);

        // キャッシュ読み込みの最中に、待機期限が過ぎて TileJSON が取得できるようになる（識別子の確定後に版が変わり得る）
        var cache = new AdvancingTileCache(() =>
        {
            time.Advance(OpenFreeMapTileSource.ResolveRetryInterval + TimeSpan.FromSeconds(1));
            tileJsonAvailable = true;
        });
        var cached = new CachedVectorTileSource(source, cache);

        byte[]? tile = await cached.GetTileAsync(14, 1, 2, CancellationToken.None);

        Assert.Equal([9], tile);

        // 識別子（フォールバックの版）と同じ版の URL で取得し、同じ版の鍵で保存する
        Assert.Equal(
            new Uri("https://tiles.openfreemap.org/planet/20261018_080001_pt/14/1/2.pbf"),
            Assert.Single(requestedTiles));
        Assert.Equal(
            VectorTileCacheKey.Create($"openfreemap:{OpenFreeMapTileSource.FallbackUrlTemplate}", 14, 1, 2),
            Assert.Single(cache.WrittenKeys));
    }

    [Fact]
    public async Task TileJSON取得中の呼出し元キャンセルは状態を確定せず再実行時に最新URLを解決する()
    {
        int tileJsonRequestCount = 0;
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri == new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl))
            {
                tileJsonRequestCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleTileJson),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            };
        });

        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client);

        var canceledToken = new CancellationToken(canceled: true);

        // 呼出し元がキャンセルを要求した場合は OperationCanceledException がスローされる
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ResolveUrlTemplateAsync(canceledToken));

        // 有効なトークンで再実行すると TileJSON を再取得して最新 URL を解決する
        string resolved = await source.ResolveUrlTemplateAsync(CancellationToken.None);

        Assert.Equal("https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf", resolved);
        Assert.Equal("openfreemap:https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf", source.SourceId);
        Assert.True(tileJsonRequestCount >= 1);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.now;

        public void Advance(TimeSpan duration) => this.now += duration;
    }

    private sealed class MemoryTileCache : ITileCache
    {
        private readonly Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(this.entries.TryGetValue(key, out byte[]? value) ? value : null);

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
        {
            this.entries[key] = content;
            return Task.CompletedTask;
        }
    }

    /// <summary>最初の読み取りだけ呼び出し元のキャンセルとして中断するキャッシュ。</summary>
    private sealed class CancelFirstReadTileCache(ITileCache inner) : ITileCache
    {
        private bool cancelled;

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
        {
            if (!this.cancelled)
            {
                this.cancelled = true;
                throw new OperationCanceledException();
            }

            return inner.TryReadAsync(key, cancellationToken);
        }

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
            => inner.WriteAsync(key, content, cancellationToken);
    }

    /// <summary>読み取りの最中に任意の処理（時刻の進行など）を行い、常にミスを返すキャッシュ。</summary>
    private sealed class AdvancingTileCache(Action onRead) : ITileCache
    {
        public List<string> WrittenKeys { get; } = [];

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
        {
            onRead();
            return Task.FromResult<byte[]?>(null);
        }

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
        {
            this.WrittenKeys.Add(key);
            return Task.CompletedTask;
        }
    }
}
