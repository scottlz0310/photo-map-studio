using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class CachedVectorTileSourceTests
{
    [Fact]
    public async Task キャッシュヒット時は元ソースを呼び出さずキャッシュのデータを返す()
    {
        byte[] cachedBytes = [1, 2, 3];
        var stubCache = new MemoryTileCache();
        string key = VectorTileCacheKey.Create("test-source", 14, 100, 200);
        await stubCache.WriteAsync(key, cachedBytes, CancellationToken.None);

        var stubSource = new StubVectorTileSource("test-source", 14, (_, _, _) => throw new InvalidOperationException("呼び出されてはならない"));
        var cachedSource = new CachedVectorTileSource(stubSource, stubCache);

        byte[]? result = await cachedSource.GetTileAsync(14, 100, 200, CancellationToken.None);

        Assert.Equal(cachedBytes, result);
    }

    [Fact]
    public async Task キャッシュミス時は元ソースから取得してキャッシュへ保存する()
    {
        byte[] fetchedBytes = [4, 5, 6];
        var stubCache = new MemoryTileCache();
        int fetchCount = 0;
        var stubSource = new StubVectorTileSource("test-source", 14, (_, _, _) =>
        {
            fetchCount++;
            return fetchedBytes;
        });

        var cachedSource = new CachedVectorTileSource(stubSource, stubCache);

        byte[]? result = await cachedSource.GetTileAsync(14, 100, 200, CancellationToken.None);

        Assert.Equal(fetchedBytes, result);
        Assert.Equal(1, fetchCount);

        // キャッシュに保存されていることを検証
        string key = VectorTileCacheKey.Create("test-source", 14, 100, 200);
        byte[]? cached = await stubCache.TryReadAsync(key, CancellationToken.None);
        Assert.Equal(fetchedBytes, cached);
    }

    [Fact]
    public async Task 元ソースがnullを返した場合はキャッシュへ保存しない()
    {
        var stubCache = new MemoryTileCache();
        var stubSource = new StubVectorTileSource("test-source", 14, (_, _, _) => null);

        var cachedSource = new CachedVectorTileSource(stubSource, stubCache);

        byte[]? result = await cachedSource.GetTileAsync(14, 100, 200, CancellationToken.None);

        Assert.Null(result);
        string key = VectorTileCacheKey.Create("test-source", 14, 100, 200);
        Assert.Null(await stubCache.TryReadAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task キャッシュ書き込みに失敗しても取得データは正常に返る()
    {
        byte[] fetchedBytes = [7, 8, 9];
        var failingCache = new FailingWriteTileCache(new IOException("Disk full"));
        var stubSource = new StubVectorTileSource("test-source", 14, (_, _, _) => fetchedBytes);

        var cachedSource = new CachedVectorTileSource(stubSource, failingCache);

        byte[]? result = await cachedSource.GetTileAsync(14, 100, 200, CancellationToken.None);

        Assert.Equal(fetchedBytes, result);
    }

    [Fact]
    public async Task 異なるSourceIdのソースはキャッシュが分離される()
    {
        byte[] bytesA = [1, 1, 1];
        byte[] bytesB = [2, 2, 2];
        var stubCache = new MemoryTileCache();

        var sourceA = new StubVectorTileSource("source-a", 14, (_, _, _) => bytesA);
        var sourceB = new StubVectorTileSource("source-b", 14, (_, _, _) => bytesB);

        var cachedA = new CachedVectorTileSource(sourceA, stubCache);
        var cachedB = new CachedVectorTileSource(sourceB, stubCache);

        byte[]? resultA = await cachedA.GetTileAsync(14, 100, 200, CancellationToken.None);
        byte[]? resultB = await cachedB.GetTileAsync(14, 100, 200, CancellationToken.None);

        Assert.Equal(bytesA, resultA);
        Assert.Equal(bytesB, resultB);

        // キャッシュキーも分離されている
        string keyA = VectorTileCacheKey.Create("source-a", 14, 100, 200);
        string keyB = VectorTileCacheKey.Create("source-b", 14, 100, 200);
        Assert.NotEqual(keyA, keyB);
        Assert.Equal(bytesA, await stubCache.TryReadAsync(keyA, CancellationToken.None));
        Assert.Equal(bytesB, await stubCache.TryReadAsync(keyB, CancellationToken.None));
    }

    private sealed class StubVectorTileSource(string sourceId, int maxZoom, Func<int, int, int, byte[]?> handler) : IVectorTileSource
    {
        public string SourceId => sourceId;
        public int MaxZoom => maxZoom;
        public Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken cancellationToken = default)
            => Task.FromResult(handler(z, x, y));
    }

    private sealed class MemoryTileCache : ITileCache
    {
        private readonly Dictionary<string, byte[]> storage = new();

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(this.storage.TryGetValue(key, out byte[]? val) ? val : null);

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
        {
            this.storage[key] = content;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingWriteTileCache(Exception error) : ITileCache
    {
        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken) => Task.FromException(error);
    }
}
