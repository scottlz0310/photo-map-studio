using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Tiles;

public class TileProviderTests
{
    private static readonly TileSource Source = TileSources.OpenStreetMap;

    [Fact]
    public async Task キャッシュにあればネットワークへ出ない()
    {
        byte[] cached = [0x01, 0x02];
        var cache = new FakeTileCache();
        cache.Entries[TileCacheKey.Create(Source.UrlTemplate, 15, 1, 2)] = cached;
        var client = new FakeTileClient([0xFF]);
        var provider = new TileProvider(client, cache);

        byte[] actual = await provider.GetTileAsync(Source, 15, 1, 2, CancellationToken.None);

        Assert.Equal(cached, actual);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task キャッシュになければ取得して保存する()
    {
        byte[] fetched = [0x0A, 0x0B];
        var cache = new FakeTileCache();
        var client = new FakeTileClient(fetched);
        var provider = new TileProvider(client, cache);

        byte[] actual = await provider.GetTileAsync(Source, 15, 1, 2, CancellationToken.None);

        Assert.Equal(fetched, actual);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(fetched, cache.Entries[TileCacheKey.Create(Source.UrlTemplate, 15, 1, 2)]);
    }

    [Fact]
    public async Task タイルソースが異なればキャッシュを共有しない()
    {
        var cache = new FakeTileCache();
        var client = new FakeTileClient([0x0A]);
        var provider = new TileProvider(client, cache);

        await provider.GetTileAsync(TileSources.OpenStreetMap, 15, 1, 2, CancellationToken.None);
        await provider.GetTileAsync(TileSources.GsiPale, 15, 1, 2, CancellationToken.None);

        Assert.Equal(2, client.CallCount);
        Assert.Equal(2, cache.Entries.Count);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(19)]
    public async Task 対応ズーム範囲外は例外を投げる(int zoom)
    {
        var provider = new TileProvider(new FakeTileClient([0x01]), new FakeTileCache());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.GetTileAsync(TileSources.GsiPale, zoom, 1, 2, CancellationToken.None));
    }

    [Fact]
    public async Task 取得失敗は代替タイルに置き換えず伝播する()
    {
        var client = new FakeTileClient(new TileFetchException("取得できません"));
        var cache = new FakeTileCache();
        var provider = new TileProvider(client, cache);

        await Assert.ThrowsAsync<TileFetchException>(
            () => provider.GetTileAsync(Source, 15, 1, 2, CancellationToken.None));
        Assert.Empty(cache.Entries);
    }

    private sealed class FakeTileCache : ITileCache
    {
        public Dictionary<string, byte[]> Entries { get; } = [];

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(this.Entries.TryGetValue(key, out byte[]? content) ? content : null);

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
        {
            this.Entries[key] = content;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTileClient : ITileClient
    {
        private readonly byte[]? content;
        private readonly Exception? failure;

        public FakeTileClient(byte[] content) => this.content = content;

        public FakeTileClient(Exception failure) => this.failure = failure;

        public int CallCount { get; private set; }

        public Task<byte[]> GetTileAsync(
            TileSource source,
            int zoom,
            int x,
            int y,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            return this.failure is null
                ? Task.FromResult(this.content!)
                : Task.FromException<byte[]>(this.failure);
        }
    }
}
