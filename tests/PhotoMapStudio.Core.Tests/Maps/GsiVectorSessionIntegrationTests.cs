using System.Net;

using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Maps;

public sealed class GsiVectorSessionIntegrationTests
{
    private static readonly GeoCoordinate Tokyo = new(35.681166, 139.767111);

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private sealed class MemoryTileCache : ITileCache
    {
        private readonly Dictionary<string, byte[]> storage = new(StringComparer.Ordinal);

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
        {
            this.storage.TryGetValue(key, out byte[]? val);
            return Task.FromResult(val);
        }

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
        {
            this.storage[key] = content;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task 実デコレータ構成でHTTP障害時は正確に10回目の失敗でセッション停止する()
    {
        int networkRequestCount = 0;
        using var handler = new StubHttpMessageHandler(req =>
        {
            Interlocked.Increment(ref networkRequestCount);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        using var httpClient = new HttpClient(handler);
        var cache = new MemoryTileCache();

        // 実際の DI 構成と同様のデコレータ: CachedVectorTileSource -> GsiVectorTileSource (HttpPmtilesByteSource)
        IVectorTileSource gsiSource = GsiVectorTileSource.Create(httpClient);
        var cachedSource = new CachedVectorTileSource(gsiSource, cache);

        using var gsiStyle = new GsiVectorStyle();
        var renderer = new VectorMapRenderer(cachedSource, gsiStyle);

        // SkiaMapImageComposer 経由で合成
        var dummyRasterProvider = new DummyRasterProvider();
        var composer = new SkiaMapImageComposer(dummyRasterProvider, renderer);

        var session = new TileFetchSession(TimeProvider.System);
        Exception? lastException = null;

        // 10 回連続で失敗させる
        for (int i = 0; i < 10; i++)
        {
            var request = new MapCompositionRequest
            {
                Center = Tokyo,
                TileSource = TileSources.GsiVector,
                TileSession = session,
                Width = 200,
                Height = 200,
            };

            // 5 回目の時点では二重計数されず停止していないこと
            if (i == 4)
            {
                Assert.False(session.IsStopped, "5回目の要求時点ではセッションが停止していてはなりません (二重計数防止)");
            }

            try
            {
                await composer.ComposeAsync(request, CancellationToken.None);
            }
            catch (Exception ex) when (ex is TileFetchException or InvalidOperationException)
            {
                lastException = ex;
            }
        }

        // 正確に 10 回目で IsStopped == true となること
        Assert.True(session.IsStopped, "10回目の失敗でセッションが停止状態になる必要があります");
        Assert.IsType<InvalidOperationException>(lastException);
        int requestCountAtStop = networkRequestCount;

        // 停止後にさらに要求しても即座に InvalidOperationException となりネットワーク要求は一切増加しない
        for (int i = 0; i < 2; i++)
        {
            var request = new MapCompositionRequest
            {
                Center = Tokyo,
                TileSource = TileSources.GsiVector,
                TileSession = session,
                Width = 200,
                Height = 200,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => composer.ComposeAsync(request, CancellationToken.None));
        }

        Assert.Equal(requestCountAtStop, networkRequestCount);
    }

    private sealed class DummyRasterProvider : ITileProvider
    {
        public Task<byte[]> GetTileAsync(TileSource source, int zoom, int x, int y, CancellationToken cancellationToken, TileFetchSession? session = null)
            => throw new NotSupportedException();
    }
}
