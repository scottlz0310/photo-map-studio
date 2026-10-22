using System.Net;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class OpenFreeMapTileSourceTests
{
    [Fact]
    public async Task 正常応答のバイト列をそのまま返す()
    {
        byte[] expected = [0x1F, 0x8B, 0x08, 0x00];
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, expected);
        using var client = factory.CreateClient("test");
        var source = new OpenFreeMapTileSource(client);

        byte[]? actual = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task テンプレート座標を置換したURLへGET要求する()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, [1]);
        using var client = factory.CreateClient("test");
        var source = new OpenFreeMapTileSource(client);

        await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Single(factory.Requests);
        Assert.Equal(
            new Uri("https://tiles.openfreemap.org/planet/20261018_080001_pt/14/14675/5991.pbf"),
            factory.Requests[0].RequestUri);
        Assert.Equal(HttpMethod.Get, factory.Requests[0].Method);
    }

    [Fact]
    public async Task 固有のUserAgentを表明する()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, [1]);
        using var client = factory.CreateClient("test");
        var source = new OpenFreeMapTileSource(client);

        await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(UserAgentProvider.Value, factory.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task 存在しないタイル404の場合はnullを返す()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.NotFound);
        using var client = factory.CreateClient("test");
        var source = new OpenFreeMapTileSource(client);

        byte[]? result = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task サーバーエラー応答時はTileFetchExceptionをスローする(HttpStatusCode status)
    {
        using var factory = StubHttpClientFactory.WithStatus(status);
        using var client = factory.CreateClient("test");
        var source = new OpenFreeMapTileSource(client);

        TileFetchException ex = await Assert.ThrowsAsync<TileFetchException>(
            () => source.GetTileAsync(14, 14675, 5991, CancellationToken.None));

        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public void 既定のSourceIdはopenfreemapでMaxZoomは14である()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK);
        using var client = factory.CreateClient("test");
        var source = new OpenFreeMapTileSource(client);

        Assert.Equal("openfreemap", source.SourceId);
        Assert.Equal(14, source.MaxZoom);
    }
}
