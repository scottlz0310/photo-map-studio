using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

public class DecodedTileCacheTests
{
    private const int BaseZoom = 14;
    private const int TileX = 14370;
    private const int TileY = 6488;

    [Fact]
    public void 容量を超えたら最近使っていないタイルから捨てる()
    {
        var cache = new DecodedTileCache(1500);
        VectorTile a = NewTile();
        VectorTile b = NewTile();
        VectorTile c = NewTile();
        cache.Add(Key(1), a, 600);
        cache.Add(Key(2), b, 600);

        // a を使うと、b が最も古くなる
        Assert.True(cache.TryGet(Key(1), out _));
        cache.Add(Key(3), c, 600);

        Assert.True(cache.TryGet(Key(1), out VectorTile? keptA));
        Assert.Same(a, keptA);
        Assert.False(cache.TryGet(Key(2), out _));
        Assert.True(cache.TryGet(Key(3), out _));
        Assert.Equal(2, cache.Count);
        Assert.Equal(1200, cache.UsedBytes);
    }

    [Fact]
    public void 追加した1件は容量を超えていても残す()
    {
        var cache = new DecodedTileCache(100);

        cache.Add(Key(1), NewTile(), 5000);

        Assert.True(cache.TryGet(Key(1), out _));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void 同じキーを追加し直すと大きさを置き換える()
    {
        var cache = new DecodedTileCache(10_000);
        cache.Add(Key(1), NewTile(), 700);

        VectorTile replacement = NewTile();
        cache.Add(Key(1), replacement, 300);

        Assert.Equal(1, cache.Count);
        Assert.Equal(300, cache.UsedBytes);
        Assert.True(cache.TryGet(Key(1), out VectorTile? actual));
        Assert.Same(replacement, actual);
    }

    [Fact]
    public void 取得元が違えば同じ座標でも混ざらない()
    {
        var cache = new DecodedTileCache(10_000);
        cache.Add(new DecodedTileCache.TileKey("source-a", 14, 1, 1), NewTile(), 100);

        Assert.False(cache.TryGet(new DecodedTileCache.TileKey("source-b", 14, 1, 1), out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 容量は正の値だけを受け付ける(long capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecodedTileCache(capacity));
    }

    [Fact]
    public void 複数スレッドから同時に使っても壊れない()
    {
        var cache = new DecodedTileCache(50_000);
        VectorTile tile = NewTile();

        Parallel.For(0, 2000, i =>
        {
            cache.Add(Key(i % 300), tile, 500);
            cache.TryGet(Key((i * 7) % 300), out _);
        });

        Assert.InRange(cache.UsedBytes, 1, 50_000);
        Assert.Equal(cache.UsedBytes, cache.Count * 500L);
    }

    [Fact]
    public async Task 同じタイルの2回目の描画は取得もデコードもせず同じ画像になる()
    {
        int fetches = 0;
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, x, y) =>
        {
            Interlocked.Increment(ref fetches);
            return x == TileX && y == TileY ? MvtTestTile.CreateWater(rightEdge: 3072) : null;
        });
        var renderer = new VectorMapRenderer(source, style, new DecodedTileCache(10_000_000));
        VectorViewRequest request = Request();

        using VectorViewResult first = await renderer.RenderViewAsync(request);
        int fetchesAfterFirst = fetches;
        using VectorViewResult second = await renderer.RenderViewAsync(request);

        Assert.Equal(1, fetchesAfterFirst);
        Assert.Equal(fetchesAfterFirst, fetches);
        Assert.Equal(1, second.RequestedTileCount);
        Assert.True(second.IsComplete);
        using SKBitmap a = SKBitmap.FromImage(first.Image);
        using SKBitmap b = SKBitmap.FromImage(second.Image);
        Assert.Equal(a.Bytes, b.Bytes);
    }

    [Fact]
    public async Task キャッシュを指定しなければ毎回取得する()
    {
        int fetches = 0;
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) =>
        {
            Interlocked.Increment(ref fetches);
            return MvtTestTile.CreateWater();
        });
        var renderer = new VectorMapRenderer(source, style);

        using VectorViewResult first = await renderer.RenderViewAsync(Request());
        using VectorViewResult second = await renderer.RenderViewAsync(Request());

        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task 欠落したタイルは覚えず後から取得できる()
    {
        bool available = false;
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, x, y) =>
            available && x == TileX && y == TileY ? MvtTestTile.CreateWater() : null);
        var renderer = new VectorMapRenderer(source, style, new DecodedTileCache(10_000_000));
        VectorViewRequest request = Request();

        // 最初は配信範囲外（描けるタイルがない）
        await Assert.ThrowsAsync<PhotoMapStudio.Tiles.TileFetchException>(() => renderer.RenderViewAsync(request));

        available = true;
        using VectorViewResult result = await renderer.RenderViewAsync(request);

        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task 従来のAPIもキャッシュを使う()
    {
        int fetches = 0;
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) =>
        {
            Interlocked.Increment(ref fetches);
            return MvtTestTile.CreateWater();
        });
        var renderer = new VectorMapRenderer(source, style, new DecodedTileCache(10_000_000));
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);

        using SKBitmap first = await renderer.RenderMapAsync(latitude, longitude, BaseZoom, 64, 64);
        using SKBitmap second = await renderer.RenderMapAsync(latitude, longitude, BaseZoom, 64, 64);

        Assert.Equal(1, fetches);
        Assert.Equal(first.Bytes, second.Bytes);
    }

    [Fact]
    public async Task 動的に版を解決する取得元のタイルを別の版の固定の取得元へ返さない()
    {
        // 解決の前の識別子（フォールバックの版 v1）で鍵を作ると、解決後の版 v2 のタイルが v1 の鍵で保存され、
        // 同じキャッシュを共有する v1 固定の取得元に、v2 のタイルが返ってしまう
        using var style = new OpenMapTilesStyle();
        var cache = new DecodedTileCache(10_000_000);
        var dynamicSource = new VersionedSource("v1", resolvedAtFirstCall: "v2", tileFor: id => id == "v2" ? MvtTestTile.CreateWater() : null);
        var fixedSource = new VersionedSource("v1", resolvedAtFirstCall: null, tileFor: _ => null);

        using VectorViewResult latest = await new VectorMapRenderer(dynamicSource, style, cache).RenderViewAsync(Request());

        Assert.True(latest.IsComplete);
        await Assert.ThrowsAsync<PhotoMapStudio.Tiles.TileFetchException>(
            () => new VectorMapRenderer(fixedSource, style, cache).RenderViewAsync(Request()));
    }

    [Fact]
    public async Task 解決に失敗した後に再解決で版が変わったら新しい版のタイルを取得する()
    {
        using var style = new OpenMapTilesStyle();
        var cache = new DecodedTileCache(10_000_000);
        var source = new VersionedSource("v1", resolvedAtFirstCall: null, tileFor: id => id == "v2" ? MvtTestTile.CreateWater() : MvtTestTile.CreateWater(rightEdge: 1024));
        var renderer = new VectorMapRenderer(source, style, cache);

        // 解決に失敗している間は v1（フォールバック）のタイルを描く
        using VectorViewResult first = await renderer.RenderViewAsync(Request());
        using SKBitmap before = SKBitmap.FromImage(first.Image);
        Assert.NotEqual(new SKColor(0xA0, 0xC8, 0xF0), before.GetPixel(120, 64));

        // 再解決で最新の版になったら、v1 のキャッシュを使わず v2 のタイルを取得する
        source.Resolve("v2");
        using VectorViewResult second = await renderer.RenderViewAsync(Request());
        using SKBitmap after = SKBitmap.FromImage(second.Image);

        Assert.Equal(new SKColor(0xA0, 0xC8, 0xF0), after.GetPixel(120, 64));
        Assert.Equal(2, source.FetchCalls);
    }

    [Fact]
    public async Task 取得の間に識別子が変わったらそのタイルは保存しない()
    {
        using var style = new OpenMapTilesStyle();
        var cache = new DecodedTileCache(10_000_000);
        var source = new VersionedSource("v1", resolvedAtFirstCall: null, tileFor: _ => MvtTestTile.CreateWater());
        source.OnFetched = () => source.Resolve("v2");
        var renderer = new VectorMapRenderer(source, style, cache);

        using VectorViewResult result = await renderer.RenderViewAsync(Request());

        Assert.True(result.IsComplete);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task 通信を止めている間は識別子を解決せず解決前の識別子の鍵を使う()
    {
        using var style = new OpenMapTilesStyle();
        var cache = new DecodedTileCache(10_000_000);
        var source = new VersionedSource("v1", resolvedAtFirstCall: "v2", tileFor: _ => MvtTestTile.CreateWater());
        var session = new PhotoMapStudio.Tiles.TileFetchSession(TimeProvider.System) { AllowIncompleteVectorMaps = true };
        for (int i = 0; i < PhotoMapStudio.Tiles.TileFetchSession.FailureLimit; i++)
        {
            session.RecordFailure(new PhotoMapStudio.Tiles.TileFetchException("通信失敗", new Uri("https://example.test/tile"), System.Net.HttpStatusCode.ServiceUnavailable, null));
        }

        Assert.True(session.IsCacheOnly);
        using VectorViewResult result = await new VectorMapRenderer(source, style, cache)
            .RenderViewAsync(Request() with { Session = session, AllowIncompleteMaps = true });

        Assert.Equal(0, source.ResolveCalls);
        Assert.Equal(1, cache.Count);
    }

    private static DecodedTileCache.TileKey Key(int x) => new("test-source", 14, x, 0);

    private static VectorTile NewTile()
    {
        Assert.True(VectorTileDecoder.TryDecode(MvtTestTile.CreateWater(), out VectorTile tile));
        return tile;
    }

    private static VectorViewRequest Request()
    {
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);
        return new VectorViewRequest(latitude, longitude, 14.2, 128, 128);
    }

    /// <summary>
    /// OpenFreeMap のように、識別子（版）を非同期に解決する取得元。解決前の <see cref="SourceId"/> はフォールバックの版を返し、
    /// 解決（<see cref="GetSourceIdAsync"/>）で最新の版になる。タイルは、指定された識別子の版で返す。
    /// </summary>
    private sealed class VersionedSource(string initialId, string? resolvedAtFirstCall, Func<string, byte[]?> tileFor) : IVectorTileSource
    {
        private string effectiveId = initialId;
        private bool resolved;

        public int ResolveCalls { get; private set; }

        public int FetchCalls { get; private set; }

        public Action? OnFetched { get; set; }

        public string SourceId => this.effectiveId;

        public int MaxZoom => BaseZoom;

        public void Resolve(string id) => this.effectiveId = id;

        public Task<string> GetSourceIdAsync(CancellationToken cancellationToken = default)
        {
            this.ResolveCalls++;
            if (!this.resolved && resolvedAtFirstCall is not null)
            {
                this.resolved = true;
                this.effectiveId = resolvedAtFirstCall;
            }

            return Task.FromResult(this.effectiveId);
        }

        public Task<byte[]?> GetTileAsync(int z, int x, int y, PhotoMapStudio.Tiles.TileFetchSession? session, CancellationToken cancellationToken = default)
            => this.Fetch(this.effectiveId, x, y);

        public Task<byte[]?> GetTileForSourceIdAsync(int z, int x, int y, string resolvedSourceId, PhotoMapStudio.Tiles.TileFetchSession? session, CancellationToken cancellationToken = default)
            => this.Fetch(resolvedSourceId, x, y);

        private Task<byte[]?> Fetch(string id, int x, int y)
        {
            this.FetchCalls++;
            byte[]? tile = x == TileX && y == TileY ? tileFor(id) : null;
            this.OnFetched?.Invoke();
            return Task.FromResult(tile);
        }
    }

    private static (double Latitude, double Longitude) TileToLatLon(double tileX, double tileY, int zoom)
    {
        double n = Math.Pow(2, zoom);
        double longitude = (tileX / n * 360.0) - 180.0;
        double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * tileY / n)))) * 180.0 / Math.PI;
        return (latitude, longitude);
    }
}
