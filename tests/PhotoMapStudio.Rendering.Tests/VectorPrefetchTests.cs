using System.Collections.Concurrent;
using System.Net;
using System.Text;

using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles;

namespace PhotoMapStudio.Rendering.Tests;

public class VectorPrefetchTests
{
    private const int BaseZoom = 14;
    private const int TileX = 14370;
    private const int TileY = 6488;

    [Theory]
    [InlineData(5.25, 7.75, 4, new[] { 4, 7, 5, 7, 4, 8, 5, 8 })] // 中心を囲む 2×2
    [InlineData(5.75, 7.25, 4, new[] { 5, 6, 6, 6, 5, 7, 6, 7 })]
    [InlineData(5.25, 0.25, 4, new[] { 4, 0, 5, 0 })] // 北の範囲外は飛ばす
    [InlineData(5.25, 15.75, 4, new[] { 4, 15, 5, 15 })] // 南の範囲外は飛ばす
    [InlineData(0.25, 3.75, 3, new[] { 7, 3, 0, 3, 7, 4, 0, 4 })] // 西の端は東の端へ折り返す
    [InlineData(7.75, 3.75, 3, new[] { 7, 3, 0, 3, 7, 4, 0, 4 })] // 東の端は西の端へ折り返す
    [InlineData(0.5, 0.5, 0, new[] { 0, 0 })] // ズーム 0 は折り返しで 1 枚にまとまる
    public void 中心を囲む2x2のタイルを選び範囲外は飛ばし経度は折り返す(double tileX, double tileY, int zoom, int[] expectedPairs)
    {
        ArgumentNullException.ThrowIfNull(expectedPairs);
        (double latitude, double longitude) = TileToLatLon(tileX, tileY, zoom);

        IReadOnlyList<(int X, int Y)> actual = PrefetchTileSelector.Around(latitude, longitude, zoom);

        var expected = new List<(int X, int Y)>();
        for (int i = 0; i < expectedPairs.Length; i += 2)
        {
            expected.Add((expectedPairs[i], expectedPairs[i + 1]));
        }

        Assert.Equal(expected.Distinct().OrderBy(t => t).ToList(), actual.OrderBy(t => t).ToList());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(90.0)]
    [InlineData(-90.0)]
    public void 緯度が範囲外や非数でも例外にせずタイルを返さない(double latitude)
    {
        IReadOnlyList<(int X, int Y)> actual = PrefetchTileSelector.Around(latitude, 139.0, 10);

        Assert.Empty(actual);
    }

    [Fact]
    public async Task 中心を囲む4枚を取得してキャッシュへ入れ描画では取得しない()
    {
        var fetched = new ConcurrentBag<(int Z, int X, int Y)>();
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (z, x, y) =>
        {
            fetched.Add((z, x, y));
            return MvtTestTile.CreateWater();
        });
        var cache = new DecodedTileCache(10_000_000);
        var renderer = new VectorMapRenderer(source, style, cache);

        await renderer.PrefetchAsync(PrefetchRequest());

        Assert.Equal(
            new[] { (BaseZoom, TileX, TileY), (BaseZoom, TileX, TileY + 1), (BaseZoom, TileX + 1, TileY), (BaseZoom, TileX + 1, TileY + 1) },
            fetched.OrderBy(t => t).ToArray());
        Assert.Equal(4, cache.Count);

        // 事前デコード済みのタイルだけで描けるので、描画は取得しない
        using VectorViewResult result = await renderer.RenderViewAsync(RenderRequest());
        Assert.True(result.IsComplete);
        Assert.Equal(4, fetched.Count);
    }

    [Fact]
    public async Task 取得ズームは連続ズームの切り捨てで取得元の最大ズームで頭打ちにする()
    {
        var zooms = new ConcurrentBag<int>();
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (z, _, _) =>
        {
            zooms.Add(z);
            return MvtTestTile.CreateWater();
        });
        var renderer = new VectorMapRenderer(source, style, new DecodedTileCache(10_000_000));
        (double latitude, double longitude) = TileToLatLon(TileX + 0.75, TileY + 0.75, BaseZoom);

        // 最大ズームより深い連続ズーム（過拡大）
        await renderer.PrefetchAsync(new VectorPrefetchRequest(latitude, longitude, 17.6));

        Assert.NotEmpty(zooms);
        Assert.All(zooms, z => Assert.Equal(BaseZoom, z));
    }

    [Fact]
    public async Task 取得済みのタイルは取得し直さない()
    {
        int fetches = 0;
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) =>
        {
            Interlocked.Increment(ref fetches);
            return MvtTestTile.CreateWater();
        });
        var renderer = new VectorMapRenderer(source, style, new DecodedTileCache(10_000_000));

        await renderer.PrefetchAsync(PrefetchRequest());
        await renderer.PrefetchAsync(PrefetchRequest());

        Assert.Equal(4, fetches);
    }

    [Fact]
    public async Task キャッシュを指定していなければ失敗を隠さず例外にする()
    {
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) => MvtTestTile.CreateWater());
        var renderer = new VectorMapRenderer(source, style);

        await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.PrefetchAsync(PrefetchRequest()));
    }

    [Fact]
    public async Task キャンセル済みなら取得もキャッシュへの保存もせずキャンセルで終わる()
    {
        int fetches = 0;
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) =>
        {
            Interlocked.Increment(ref fetches);
            return MvtTestTile.CreateWater();
        });
        var cache = new DecodedTileCache(10_000_000);
        var renderer = new VectorMapRenderer(source, style, cache);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.PrefetchAsync(PrefetchRequest(), cts.Token));

        Assert.Equal(0, fetches);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task 途中でキャンセルされたら残りは取得せず保存済みのタイルは完成したものだけになる()
    {
        int fetches = 0;
        using var cts = new CancellationTokenSource();
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) =>
        {
            // 1 枚目の取得の直後に取り消す
            if (Interlocked.Increment(ref fetches) == 1)
            {
                cts.Cancel();
            }

            return MvtTestTile.CreateWater();
        });
        var cache = new DecodedTileCache(10_000_000);
        var renderer = new VectorMapRenderer(source, style, cache);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.PrefetchAsync(PrefetchRequest(), cts.Token));

        Assert.Equal(1, fetches);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task 取得の失敗は握りつぶさず例外で返す()
    {
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) =>
            throw new TileFetchException("通信失敗", new Uri("https://example.test/tile"), HttpStatusCode.ServiceUnavailable, null));
        var cache = new DecodedTileCache(10_000_000);
        var renderer = new VectorMapRenderer(source, style, cache);

        var exception = await Assert.ThrowsAsync<TileFetchException>(() => renderer.PrefetchAsync(PrefetchRequest()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task ベクタータイルとして読めない内容は握りつぶさず例外で返す()
    {
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) => Encoding.UTF8.GetBytes("<html>404 Not Found</html>"));
        var cache = new DecodedTileCache(10_000_000);
        var renderer = new VectorMapRenderer(source, style, cache);

        var exception = await Assert.ThrowsAsync<TileFetchException>(() => renderer.PrefetchAsync(PrefetchRequest()));

        Assert.Null(exception.StatusCode);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task 配信範囲外のタイルは入れるものがないので飛ばして正常に終わる()
    {
        using var style = new OpenMapTilesStyle();
        // 4 枚のうち 1 枚だけが配信範囲内
        var source = new MockVectorTileSource(BaseZoom, (_, x, y) => x == TileX && y == TileY ? MvtTestTile.CreateWater() : null);
        var cache = new DecodedTileCache(10_000_000);
        var renderer = new VectorMapRenderer(source, style, cache);

        await renderer.PrefetchAsync(PrefetchRequest());

        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task 配信元に応じた取得元をリゾルバーで選ぶ()
    {
        using var style = new OpenMapTilesStyle();
        var unused = new MockVectorTileSource(BaseZoom, (_, _, _) => throw new InvalidOperationException("既定の取得元は使わない"));
        int resolvedFetches = 0;
        var resolved = new MockVectorTileSource(BaseZoom, (_, _, _) =>
        {
            Interlocked.Increment(ref resolvedFetches);
            return MvtTestTile.CreateWater();
        });
        TileSource? requested = null;
        var tileSource = new TileSource("test", "https://example.test/{z}/{x}/{y}.pbf", 0, BaseZoom, "test", new TileRateLimit(1, TimeSpan.Zero), isVector: true);
        var renderer = new VectorMapRenderer(unused, style, source =>
        {
            requested = source;
            return (resolved, style);
        }, new DecodedTileCache(10_000_000));

        await renderer.PrefetchAsync(PrefetchRequest() with { TileSource = tileSource });

        Assert.Same(tileSource, requested);
        Assert.Equal(4, resolvedFetches);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task ズームは0以上の有限値だけを受け付ける(double zoom)
    {
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) => MvtTestTile.CreateWater());
        var renderer = new VectorMapRenderer(source, style, new DecodedTileCache(10_000_000));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => renderer.PrefetchAsync(PrefetchRequest() with { Zoom = zoom }));
    }

    [Fact]
    public async Task 描画と同時に何度呼んでもキャッシュは壊れず描画も成功する()
    {
        using var style = new OpenMapTilesStyle();
        var source = new MockVectorTileSource(BaseZoom, (_, _, _) => MvtTestTile.CreateWater());
        var cache = new DecodedTileCache(10_000_000);
        var renderer = new VectorMapRenderer(source, style, cache);

        // 同じタイルを、事前デコードと描画が同時に取得・デコードする（二重のデコードは許容し、結果は壊れない）
        Task[] work = Enumerable.Range(0, 16)
            .Select(i => i % 2 == 0
                ? renderer.PrefetchAsync(PrefetchRequest())
                : Task.Run(async () =>
                {
                    using VectorViewResult result = await renderer.RenderViewAsync(RenderRequest()).ConfigureAwait(false);
                    Assert.True(result.IsComplete);
                }))
            .ToArray();

        await Task.WhenAll(work);

        Assert.Equal(4, cache.Count);
    }

    // タイル座標の小数部が 0.5 だと、浮動小数点の誤差で隣のタイルへ寄るため、0.75 にする（2×2 は TileX..TileX+1、TileY..TileY+1）
    private static VectorPrefetchRequest PrefetchRequest()
    {
        (double latitude, double longitude) = TileToLatLon(TileX + 0.75, TileY + 0.75, BaseZoom);
        return new VectorPrefetchRequest(latitude, longitude, BaseZoom);
    }

    // 128px・ズーム 14.2 は、タイル（TileX, TileY）の中心を中心にすると、そのタイル 1 枚に収まる
    private static VectorViewRequest RenderRequest()
    {
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);
        return new VectorViewRequest(latitude, longitude, 14.2, 128, 128);
    }

    private static (double Latitude, double Longitude) TileToLatLon(double tileX, double tileY, int zoom)
    {
        double n = Math.Pow(2, zoom);
        double longitude = (tileX / n * 360.0) - 180.0;
        double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * tileY / n)))) * 180.0 / Math.PI;
        return (latitude, longitude);
    }
}
