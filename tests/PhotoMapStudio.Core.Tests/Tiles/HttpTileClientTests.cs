using System.Net;

using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Tiles;

public class HttpTileClientTests
{
    private static readonly TileSource Source = TileSources.OpenStreetMap;

    [Fact]
    public async Task 応答のバイト列をそのまま返す()
    {
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D];
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, content);
        var client = new HttpTileClient(factory);

        byte[] actual = await client.GetTileAsync(Source, 15, 29105, 12903, CancellationToken.None);

        Assert.Equal(content, actual);
    }

    [Fact]
    public async Task テンプレートを置換したURLへ要求する()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK);
        var client = new HttpTileClient(factory);

        await client.GetTileAsync(Source, 15, 29105, 12903, CancellationToken.None);

        Assert.Equal(
            new Uri("https://tile.openstreetmap.org/15/29105/12903.png"),
            factory.Requests[0].RequestUri);
    }

    [Fact]
    public async Task 固有のUserAgentを表明する()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK);
        var client = new HttpTileClient(factory);

        await client.GetTileAsync(Source, 15, 29105, 12903, CancellationToken.None);

        Assert.Equal(UserAgentProvider.Value, factory.Requests[0].Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task 失敗応答は例外として伝播する(HttpStatusCode statusCode)
    {
        using var factory = StubHttpClientFactory.WithStatus(statusCode);
        var client = new HttpTileClient(factory);

        TileFetchException exception = await Assert.ThrowsAsync<TileFetchException>(
            () => client.GetTileAsync(Source, 15, 29105, 12903, CancellationToken.None));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal(new Uri("https://tile.openstreetmap.org/15/29105/12903.png"), exception.RequestUri);
    }

    [Fact]
    public async Task 通信エラーは原因を保持して伝播する()
    {
        using var factory = new StubHttpClientFactory(
            _ => throw new HttpRequestException("接続できません"));
        var client = new HttpTileClient(factory);

        TileFetchException exception = await Assert.ThrowsAsync<TileFetchException>(
            () => client.GetTileAsync(Source, 15, 29105, 12903, CancellationToken.None));

        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task キャンセル済みトークンでは取得しない()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK);
        var client = new HttpTileClient(factory);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetTileAsync(Source, 15, 29105, 12903, cts.Token));
        Assert.Empty(factory.Requests);
    }
}
