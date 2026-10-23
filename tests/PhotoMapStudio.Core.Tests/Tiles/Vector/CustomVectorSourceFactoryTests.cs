using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;

using Microsoft.Extensions.Time.Testing;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public sealed class CustomVectorSourceFactoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pms-factory-{Guid.NewGuid():N}");
    private readonly OpenMapTilesStyle openMapTilesStyle = new();
    private readonly GsiVectorStyle gsiStyle = new();
    private readonly MemoryTileCache cache = new();
    private readonly List<HttpClient> clients = [];

    public CustomVectorSourceFactoryTests() => Directory.CreateDirectory(this.directory);

    public void Dispose()
    {
        this.openMapTilesStyle.Dispose();
        this.gsiStyle.Dispose();
        foreach (HttpClient client in this.clients)
        {
            client.Dispose();
        }

        Directory.Delete(this.directory, recursive: true);
    }

    [Fact]
    public void スキーマに応じて描画スタイルを選ぶ()
    {
        using CustomVectorSourceFactory factory = this.CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var (_, openMapTiles) = factory.Resolve(VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.OpenMapTiles));
        var (_, gsi) = factory.Resolve(VectorSourceSpec.Create("https://example.com/japan.pmtiles", VectorStyleSchema.GsiOptimalBvmap));

        Assert.Same(this.openMapTilesStyle, openMapTiles);
        Assert.Same(this.gsiStyle, gsi);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(16)]
    public void URLテンプレートの取得元は指定した最大ズームを持つ(int maxZoom)
    {
        using CustomVectorSourceFactory factory = this.CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var (source, _) = factory.Resolve(VectorSourceSpec.Create("https://example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles, maxZoom));

        Assert.Equal(maxZoom, source.MaxZoom);
    }

    [Fact]
    public void 最大ズームが異なるURLテンプレートは別の取得元になる()
    {
        using CustomVectorSourceFactory factory = this.CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var (z14, _) = factory.Resolve(VectorSourceSpec.Create("https://example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles, 14));
        var (z16, _) = factory.Resolve(VectorSourceSpec.Create("https://example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles, 16));

        Assert.NotSame(z14, z16);
    }

    [Fact]
    public void 同じ指定は取得元を共有し別の指定は別の取得元になる()
    {
        using CustomVectorSourceFactory factory = this.CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var specA = VectorSourceSpec.Create("https://example.com/a.pmtiles", VectorStyleSchema.OpenMapTiles);
        var specB = VectorSourceSpec.Create("https://example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles);

        var (first, _) = factory.Resolve(specA);
        var (second, _) = factory.Resolve(VectorSourceSpec.Create("https://example.com/a.pmtiles", VectorStyleSchema.OpenMapTiles));
        var (other, _) = factory.Resolve(specB);

        Assert.Same(first, second);
        Assert.NotSame(first, other);
    }

    [Fact]
    public async Task URLテンプレートの取得元はキャッシュを通し指定のURLからタイルを取得する()
    {
        var requested = new List<Uri>();
        using CustomVectorSourceFactory factory = this.CreateFactory(request =>
        {
            requested.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        });
        var spec = VectorSourceSpec.Create("http://127.0.0.1:9/tiles/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles);
        var (source, _) = factory.Resolve(spec);

        byte[]? first = await source.GetTileAsync(14, 1, 2, session: null, CancellationToken.None);
        byte[]? second = await source.GetTileAsync(14, 1, 2, session: null, CancellationToken.None);

        Assert.Equal([1, 2, 3], first);
        Assert.Equal([1, 2, 3], second);

        // 2 回目はキャッシュ命中でネットワーク要求しない
        Assert.Equal(new Uri("http://127.0.0.1:9/tiles/14/1/2.pbf"), Assert.Single(requested));
    }

    [Fact]
    public async Task PMTilesのURLはHTTPのRange要求で取得する()
    {
        var ranges = new List<string?>();
        using CustomVectorSourceFactory factory = this.CreateFactory(request =>
        {
            ranges.Add(request.Headers.Range?.ToString());

            // ヘッダー（127 バイト）の要求に、PMTiles ではない内容を返す（取得経路の確認だけが目的）
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[127]) };
        });
        var spec = VectorSourceSpec.Create("http://localhost:9/japan.pmtiles", VectorStyleSchema.OpenMapTiles);
        var (source, _) = factory.Resolve(spec);

        // PMTiles として解釈できない内容なので失敗するが、Range 要求でヘッダーを取りに行くこと
        await Assert.ThrowsAnyAsync<Exception>(() => source.GetTileAsync(14, 1, 2, session: null, CancellationToken.None));

        Assert.Equal("bytes=0-126", ranges[0]);
    }

    [Fact]
    public async Task ファイルの取得元は別のファイルに切り替えると前のファイルを閉じる()
    {
        using CustomVectorSourceFactory factory = this.CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        string a = this.CreateFile("a.pmtiles");
        string b = this.CreateFile("b.pmtiles");

        var (sourceA, _) = factory.Resolve(VectorSourceSpec.Create(a, VectorStyleSchema.OpenMapTiles));
        var (sourceB, _) = factory.Resolve(VectorSourceSpec.Create(b, VectorStyleSchema.OpenMapTiles));

        Assert.NotSame(sourceA, sourceB);

        // 前のファイルは閉じられて、差し替え・削除できる
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sourceA.GetTileAsync(14, 1, 2, session: null, CancellationToken.None));
        File.Delete(a);
        Assert.False(File.Exists(a));
    }

    [Fact]
    public void 破棄後は取得元を解決できずファイルを閉じる()
    {
        string a = this.CreateFile("a.pmtiles");
        CustomVectorSourceFactory factory = this.CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var spec = VectorSourceSpec.Create(a, VectorStyleSchema.OpenMapTiles);
        factory.Resolve(spec);

        factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => factory.Resolve(spec));
        File.Delete(a);
        Assert.False(File.Exists(a));
    }

    [Fact]
    public async Task 他者の配信元は要求の間隔を空けループバックとファイルは待たない()
    {
        var gate = new RequestIntervalGate(new TileRateLimit(1, TimeSpan.FromMilliseconds(120)), TimeProvider.System);
        var loopbackGate = new RequestIntervalGate(TileRateLimit.Loopback, TimeProvider.System);

        var elapsed = Stopwatch.StartNew();
        using (await gate.EnterAsync(CancellationToken.None))
        {
        }

        using (await gate.EnterAsync(CancellationToken.None))
        {
        }

        TimeSpan gated = elapsed.Elapsed;

        elapsed.Restart();
        for (int i = 0; i < 20; i++)
        {
            using (await loopbackGate.EnterAsync(CancellationToken.None))
            {
            }
        }

        TimeSpan loopback = elapsed.Elapsed;

        Assert.True(gate.IsActive);
        Assert.False(loopbackGate.IsActive);
        Assert.True(gated >= TimeSpan.FromMilliseconds(90), $"2 回目の要求まで待っていない: {gated}");
        Assert.True(loopback < TimeSpan.FromMilliseconds(90), $"ループバックで待っている: {loopback}");
        gate.Dispose();
        loopbackGate.Dispose();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    public async Task 待機中のキャンセルは関門を塞がない(int intervalMilliseconds)
    {
        var clock = new FakeTimeProvider();
        TimeSpan interval = TimeSpan.FromMilliseconds(intervalMilliseconds);
        using var gate = new RequestIntervalGate(new TileRateLimit(1, interval), clock);
        using (await gate.EnterAsync(CancellationToken.None))
        {
        }

        using var cancellation = new CancellationTokenSource();
        Task<IDisposable> pending = gate.EnterAsync(cancellation.Token);
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // キャンセル後の要求が間隔待機へ進み、時刻の進行だけで通過できることを確認する。
        Task<IDisposable> next = gate.EnterAsync(CancellationToken.None);
        Assert.False(next.IsCompleted);
        clock.Advance(interval);
        using (await next.WaitAsync(TimeSpan.FromSeconds(5)))
        {
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "作成した HttpClient は一覧に保持し、テストの破棄時に破棄する。")]
    private CustomVectorSourceFactory CreateFactory(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var client = new HttpClient(new StubHandler(respond));
        this.clients.Add(client);
        return new CustomVectorSourceFactory(client, this.cache, this.openMapTilesStyle, this.gsiStyle);
    }

    private string CreateFile(string name)
    {
        string path = Path.Combine(this.directory, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
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
}
