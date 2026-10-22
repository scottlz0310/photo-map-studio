using System.Net;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class OpenFreeMapTileSourceTests
{
    private const string SampleTileJson = """
        {
            "tilejson": "3.0.0",
            "tiles": [
                "https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf"
            ]
        }
        """;

    [Fact]
    public async Task 明示的なテンプレート指定時はTileJSONを要求せず指定URLへ要求する()
    {
        byte[] expected = [0x1F, 0x8B, 0x08, 0x00];
        const string explicitTemplate = "https://custom.tiles.example.com/{z}/{x}/{y}.pbf";
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, expected);
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplate: explicitTemplate);

        byte[]? actual = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Single(factory.Requests);
        Assert.Equal(
            new Uri("https://custom.tiles.example.com/14/14675/5991.pbf"),
            factory.Requests[0].RequestUri);
        Assert.Equal($"openfreemap:{explicitTemplate}", source.SourceId);
    }

    [Fact]
    public async Task 既定構成ではTileJSONから最新URLテンプレートを解決してタイルを要求する()
    {
        byte[] expectedTile = [0x11, 0x22, 0x33];
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri == new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleTileJson),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedTile),
            };
        });

        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client);

        byte[]? actual = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(expectedTile, actual);
        Assert.Equal(2, factory.Requests.Count);

        // 1件目は TileJSON 要求
        Assert.Equal(new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl), factory.Requests[0].RequestUri);
        Assert.Equal(HttpMethod.Get, factory.Requests[0].Method);

        // 2件目は解決された URL テンプレートでのタイル要求
        Assert.Equal(
            new Uri("https://tiles.openfreemap.org/planet/20261020_080001_pt/14/14675/5991.pbf"),
            factory.Requests[1].RequestUri);

        // SourceId が解決された実効テンプレートに更新されていること
        Assert.Equal(
            "openfreemap:https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf",
            source.SourceId);
    }

    [Fact]
    public async Task TileJSON取得失敗時はFallbackUrlTemplateへフォールバックする()
    {
        byte[] expectedTile = [0xAA, 0xBB];
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri == new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedTile),
            };
        });

        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client);

        byte[]? actual = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(expectedTile, actual);
        Assert.Equal(2, factory.Requests.Count);
        Assert.Equal(new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl), factory.Requests[0].RequestUri);
        Assert.Equal(
            new Uri("https://tiles.openfreemap.org/planet/20261018_080001_pt/14/14675/5991.pbf"),
            factory.Requests[1].RequestUri);
        Assert.Equal($"openfreemap:{OpenFreeMapTileSource.FallbackUrlTemplate}", source.SourceId);
    }

    [Fact]
    public async Task 固有のUserAgentを表明する()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK, [1]);
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplate: "https://tiles.example.com/{z}/{x}/{y}.pbf");

        await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Equal(UserAgentProvider.Value, factory.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task 存在しないタイル404の場合はnullを返す()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.NotFound);
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplate: "https://tiles.example.com/{z}/{x}/{y}.pbf");

        byte[]? result = await source.GetTileAsync(14, 14675, 5991, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task タイル要求のサーバーエラー応答時はTileFetchExceptionをスローする(HttpStatusCode status)
    {
        using var factory = StubHttpClientFactory.WithStatus(status);
        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client, urlTemplate: "https://tiles.example.com/{z}/{x}/{y}.pbf");

        TileFetchException ex = await Assert.ThrowsAsync<TileFetchException>(
            () => source.GetTileAsync(14, 14675, 5991, CancellationToken.None));

        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public void URLテンプレートが異なればSourceIdも異なる()
    {
        using var factory = StubHttpClientFactory.WithStatus(HttpStatusCode.OK);
        using var client = factory.CreateClient("test");
        using var sourceA = new OpenFreeMapTileSource(client, urlTemplate: "https://server-a/{z}/{x}/{y}.pbf");
        using var sourceB = new OpenFreeMapTileSource(client, urlTemplate: "https://server-b/{z}/{x}/{y}.pbf");

        Assert.NotEqual(sourceA.SourceId, sourceB.SourceId);
        Assert.Equal("openfreemap:https://server-a/{z}/{x}/{y}.pbf", sourceA.SourceId);
        Assert.Equal("openfreemap:https://server-b/{z}/{x}/{y}.pbf", sourceB.SourceId);
        Assert.Equal(14, sourceA.MaxZoom);
    }

    [Fact]
    public async Task TileJSON取得中の呼出し元キャンセルは状態を確定せず再実行時に最新URLを解決する()
    {
        int tileJsonRequestCount = 0;
        using var factory = new StubHttpClientFactory(req =>
        {
            if (req.RequestUri == new Uri(OpenFreeMapTileSource.DefaultTileJsonUrl))
            {
                tileJsonRequestCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleTileJson),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            };
        });

        using var client = factory.CreateClient("test");
        using var source = new OpenFreeMapTileSource(client);

        var canceledToken = new CancellationToken(canceled: true);

        // 呼出し元がキャンセルを要求した場合は OperationCanceledException がスローされる
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ResolveUrlTemplateAsync(canceledToken));

        // 有効なトークンで再実行すると TileJSON を再取得して最新 URL を解決する
        string resolved = await source.ResolveUrlTemplateAsync(CancellationToken.None);

        Assert.Equal("https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf", resolved);
        Assert.Equal("openfreemap:https://tiles.openfreemap.org/planet/20261020_080001_pt/{z}/{x}/{y}.pbf", source.SourceId);
        Assert.True(tileJsonRequestCount >= 1);
    }
}
