using System.Net;

using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Maps;

public sealed class IncompleteVectorMapTests
{
    // 合成データ: 1タイル全域を覆うwaterポリゴン。写真・実在地点を含まない。
    private static byte[] WaterTile => Convert.FromHexString("1a1e0a05776174657228802012121803220e0900001a804000008040ff3f000f");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 縮退は明示的に有効にした場合だけ行い取得済みタイルとピン出典を残す(bool allow)
    {
        using var style = new OpenMapTilesStyle();
        var source = new Source((x, y) => x == 0 && y == 0 ? WaterTile : throw Offline());
        var composer = Composer(source, style);
        MapCompositionRequest request = Request(allow);
        if (!allow)
        {
            await Assert.ThrowsAsync<TileFetchException>(() => composer.ComposeAsync(request, CancellationToken.None));
            return;
        }

        MapCompositionResult result = await composer.ComposeAsync(request, CancellationToken.None);
        Assert.False(result.UsedFallback);
        Assert.Equal(TileSources.OpenFreeMap, result.TileSource);
        Assert.Equal(new IncompleteMapWarning(3, 4, false), result.Warning);
        using SKBitmap bitmap = SKBitmap.Decode(result.Png.ToArray());
        Assert.Equal(new SKColor(0xA0, 0xC8, 0xF0), bitmap.GetPixel(40, 80));
        Assert.Equal(style.BackgroundColor, bitmap.GetPixel(220, 80));
        Assert.Equal(new SKColor(0xFF, 0xE0, 0x82), bitmap.GetPixel(2, 2));
        Assert.NotEqual(style.BackgroundColor, bitmap.GetPixel(128, 128));
        Assert.NotEqual(style.BackgroundColor, bitmap.GetPixel(250, 250));
    }

    [Fact]
    public async Task 全タイルが未キャッシュでも通信失敗なら背景ピン出典警告を生成する()
    {
        using var style = new OpenMapTilesStyle();
        MapCompositionResult result = await Composer(new Source((_, _) => throw Offline()), style)
            .ComposeAsync(Request(true), CancellationToken.None);
        Assert.Equal(4, result.Warning?.MissingTileCount);
        using SKBitmap bitmap = SKBitmap.Decode(result.Png.ToArray());
        Assert.Equal(style.BackgroundColor, bitmap.GetPixel(30, 80));
        Assert.NotEqual(style.BackgroundColor, bitmap.GetPixel(128, 128));
    }

    [Fact]
    public async Task 全タイルが配信範囲外なら縮退を有効にしても404を維持する()
    {
        using var style = new OpenMapTilesStyle();
        TileFetchException failure = await Assert.ThrowsAsync<TileFetchException>(() =>
            Composer(new Source((_, _) => null), style).ComposeAsync(Request(true), CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(400)]
    public async Task 認証や設定のエラーを縮退で隠さない(int code)
    {
        using var style = new OpenMapTilesStyle();
        await Assert.ThrowsAsync<TileFetchException>(() => Composer(new Source((_, _) =>
            throw new TileFetchException("取得失敗", new Uri("https://example.test/tile"), (HttpStatusCode)code, null)), style)
            .ComposeAsync(Request(true), CancellationToken.None));
    }

    [Fact]
    public async Task 不正なタイルが混ざっていれば警告成功にせず失敗させる()
    {
        using var style = new OpenMapTilesStyle();
        var source = new Source((x, y) => x == 0 && y == 0 ? WaterTile : "invalid"u8.ToArray());
        await Assert.ThrowsAsync<TileFetchException>(() => Composer(source, style).ComposeAsync(Request(true), CancellationToken.None));
    }

    [Fact]
    public async Task 明示キャンセルが通信エラーと競合しても縮退画像を返さない()
    {
        using var cancellation = new CancellationTokenSource();
        using var style = new OpenMapTilesStyle();
        var source = new Source((_, _) =>
        {
            cancellation.Cancel();
            throw Offline();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Composer(source, style).ComposeAsync(Request(true), cancellation.Token));
    }

    [Fact]
    public async Task 連続失敗後はメタデータもタイルも通信せずキャッシュだけを使う()
    {
        using var style = new OpenMapTilesStyle();
        var source = new Source((_, _) => throw Offline());
        var cache = new Cache();
        var cached = new CachedVectorTileSource(source, cache);
        var session = new TileFetchSession(TimeProvider.System) { AllowIncompleteVectorMaps = true };
        var composer = Composer(cached, style);
        for (int i = 0; i < 5; i++)
        {
            MapCompositionResult result = await composer.ComposeAsync(Request(true) with { TileSession = session }, CancellationToken.None);
            Assert.NotNull(result.Warning);
        }
        Assert.True(session.IsCacheOnly);
        Assert.Equal(TileFetchSession.FailureLimit, source.RequestCount);
        int metadataCount = source.MetadataCount;
        int prepareCount = source.PrepareCount;

        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                await cache.WriteAsync(VectorTileCacheKey.Create(source.SourceId, 1, x, y), WaterTile, CancellationToken.None);
            }
        }
        MapCompositionResult complete = await composer.ComposeAsync(Request(true) with { TileSession = session }, CancellationToken.None);
        Assert.Null(complete.Warning);
        Assert.Equal(TileFetchSession.FailureLimit, source.RequestCount);
        Assert.Equal(metadataCount, source.MetadataCount);
        Assert.Equal(prepareCount, source.PrepareCount);
        Assert.Equal(4, session.Snapshot().CacheCount);
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    public void 一時障害の判定を認証や範囲外から区別する(int status, bool expected)
        => Assert.Equal(expected, new TileFetchException("応答", new Uri("https://example.test/tile"), (HttpStatusCode)status, null).IsTransient);

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, true)]
    [InlineData(HttpRequestError.ConnectionError, true)]
    [InlineData(HttpRequestError.ResponseEnded, true)]
    [InlineData(HttpRequestError.SecureConnectionError, false)]
    [InlineData(HttpRequestError.UserAuthenticationError, false)]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded, false)]
    public void 接続断と認証設定の障害を区別する(HttpRequestError error, bool expected)
        => Assert.Equal(expected, new TileFetchException("通信", new Uri("https://example.test/tile"), null,
            new HttpRequestException(error)).IsTransient);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 混在する認証失敗ではキャッシュ限定継続へ切り替えない(bool allow)
    {
        var session = new TileFetchSession(TimeProvider.System) { AllowIncompleteVectorMaps = allow };
        for (int i = 0; i < 9; i++)
        {
            session.RecordFailure(new TileFetchException("認証", new Uri("https://example.test/tile"), HttpStatusCode.Unauthorized, null));
        }
        session.RecordFailure(Offline());
        Assert.True(session.IsStopped);
        Assert.False(session.IsCacheOnly);
        Assert.Throws<InvalidOperationException>(session.ThrowIfStopped);
    }

    private static TileFetchException Offline()
        => new("通信断", new Uri("https://example.test/tile"), null, new HttpRequestException("通信断"));

    private static MapCompositionRequest Request(bool allow) => new()
    {
        Center = new GeoCoordinate(0, 0),
        Zoom = 1,
        Width = 256,
        Height = 256,
        TileSource = TileSources.OpenFreeMap,
        AllowWorldwideFallback = false,
        AllowIncompleteVectorMaps = allow,
        PinKind = PinKind.Crosshair,
    };

    private static SkiaMapImageComposer Composer(IVectorTileSource source, OpenMapTilesStyle style)
        => new(new NoRaster(), new VectorMapRenderer(source, style));

    private sealed class NoRaster : ITileProvider
    {
        public Task<byte[]> GetTileAsync(TileSource source, int zoom, int x, int y, CancellationToken cancellationToken, TileFetchSession? session = null)
            => throw new InvalidOperationException("ラスターへの切替は想定していません。");
    }

    private sealed class Source(Func<int, int, byte[]?> fetch) : IVectorTileSource
    {
        public string SourceId => "offline-test";
        public int MaxZoom => 1;
        public int RequestCount { get; private set; }
        public int MetadataCount { get; private set; }
        public int PrepareCount { get; private set; }
        public Task<string> GetSourceIdAsync(CancellationToken cancellationToken = default)
        {
            this.MetadataCount++;
            return Task.FromResult(this.SourceId);
        }
        public Task PrepareAsync(TileFetchSession? session, CancellationToken cancellationToken = default)
        {
            this.PrepareCount++;
            return Task.CompletedTask;
        }
        public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
        {
            this.RequestCount++;
            session?.RecordRequest();
            return Task.FromResult(fetch(x, y));
        }
    }

    private sealed class Cache : ITileCache
    {
        private readonly Dictionary<string, byte[]> entries = [];
        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(this.entries.GetValueOrDefault(key));
        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
        {
            this.entries[key] = content;
            return Task.CompletedTask;
        }
    }
}
