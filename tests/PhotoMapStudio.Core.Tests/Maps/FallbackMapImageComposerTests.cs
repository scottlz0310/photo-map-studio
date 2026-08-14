using System.Net;

using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Maps;

public class FallbackMapImageComposerTests
{
    private static readonly GeoCoordinate Paris = new(48.858370, 2.294481);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public async Task 配信範囲内ではそのまま合成する()
    {
        var inner = new RecordingComposer();
        var composer = new FallbackMapImageComposer(inner, TileSources.WorldwideFallback);

        MapCompositionResult result = await composer.ComposeAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.UsedFallback);
        Assert.Equal(TileSources.GsiPale, result.TileSource);
        Assert.Equal([TileSources.GsiPale], inner.UsedSources);
    }

    [Fact]
    public async Task 配信範囲外は代替ソースで合成し直す()
    {
        var inner = new RecordingComposer(NotFound());
        var composer = new FallbackMapImageComposer(inner, TileSources.WorldwideFallback);

        MapCompositionResult result = await composer.ComposeAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.UsedFallback);
        Assert.Equal(TileSources.OpenStreetMap, result.TileSource);
        Assert.Equal([TileSources.GsiPale, TileSources.OpenStreetMap], inner.UsedSources);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task 配信範囲外以外の失敗では切り替えない(HttpStatusCode statusCode)
    {
        var inner = new RecordingComposer(new TileFetchException(
            "取得できません",
            new Uri("https://cyberjapandata.gsi.go.jp/xyz/pale/15/0/0.png"),
            statusCode,
            null));
        var composer = new FallbackMapImageComposer(inner, TileSources.WorldwideFallback);

        await Assert.ThrowsAsync<TileFetchException>(
            () => composer.ComposeAsync(CreateRequest(), CancellationToken.None));
        Assert.Equal([TileSources.GsiPale], inner.UsedSources);
    }

    [Fact]
    public async Task 代替ソースが同じ場合は再試行しない()
    {
        var inner = new RecordingComposer(NotFound());
        var composer = new FallbackMapImageComposer(inner, TileSources.OpenStreetMap);

        await Assert.ThrowsAsync<TileFetchException>(() => composer.ComposeAsync(
            CreateRequest() with { TileSource = TileSources.OpenStreetMap },
            CancellationToken.None));
        Assert.Equal([TileSources.OpenStreetMap], inner.UsedSources);
    }

    [Fact]
    public async Task 代替ソースが対応しないズームでは切り替えない()
    {
        // 代替側の範囲を 0〜10 に絞ったソースを使い、ズーム 15 を要求する
        var narrow = new TileSource("狭いソース", "https://tile.example.com/{z}/{x}/{y}.png", 0, 10, "出典", TileRateLimit.Conservative);
        var inner = new RecordingComposer(NotFound());
        var composer = new FallbackMapImageComposer(inner, narrow);

        await Assert.ThrowsAsync<TileFetchException>(
            () => composer.ComposeAsync(CreateRequest(), CancellationToken.None));
        Assert.Equal([TileSources.GsiPale], inner.UsedSources);
    }

    [Fact]
    public async Task 代替ソースでも失敗した場合は例外を伝播する()
    {
        var inner = new RecordingComposer(NotFound(), NotFound());
        var composer = new FallbackMapImageComposer(inner, TileSources.WorldwideFallback);

        await Assert.ThrowsAsync<TileFetchException>(
            () => composer.ComposeAsync(CreateRequest(), CancellationToken.None));
        Assert.Equal([TileSources.GsiPale, TileSources.OpenStreetMap], inner.UsedSources);
    }

    private static MapCompositionRequest CreateRequest() => new()
    {
        Center = Paris,
        TileSource = TileSources.GsiPale,
        Width = 800,
        Height = 600,
        Zoom = 15,
    };

    private static TileFetchException NotFound() => new(
        "タイルの取得が HTTP 404 で失敗しました",
        new Uri("https://cyberjapandata.gsi.go.jp/xyz/pale/15/16591/11271.png"),
        HttpStatusCode.NotFound,
        null);

    private sealed class RecordingComposer(params TileFetchException?[] failures) : IMapImageComposer
    {
        private int calls;

        public List<TileSource> UsedSources { get; } = [];

        public Task<MapCompositionResult> ComposeAsync(
            MapCompositionRequest request,
            CancellationToken cancellationToken)
        {
            this.UsedSources.Add(request.TileSource);
            TileFetchException? failure = this.calls < failures.Length ? failures[this.calls] : null;
            this.calls++;

            return failure is null
                ? Task.FromResult(new MapCompositionResult(Png, request.TileSource, UsedFallback: false))
                : Task.FromException<MapCompositionResult>(failure);
        }
    }
}
