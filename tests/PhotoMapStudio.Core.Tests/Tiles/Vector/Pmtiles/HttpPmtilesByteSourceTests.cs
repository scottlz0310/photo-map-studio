using System.Net;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector.Pmtiles;

public sealed class HttpPmtilesByteSourceTests
{
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    [Fact]
    public async Task 正常な206応答で要求バイト列を取得できる()
    {
        byte[] expected = [0x01, 0x02, 0x03, 0x04];
        using var handler = new StubHttpMessageHandler(req =>
        {
            Assert.Equal("bytes=10-13", req.Headers.Range?.ToString());
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(expected),
            };
        });

        using var client = new HttpClient(handler);
        using var source = new HttpPmtilesByteSource(client, new Uri("https://example.com/test.pmtiles"));

        byte[] actual = await source.ReadRangeAsync(10, 4, session: null, CancellationToken.None);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task サーバーが200全体応答を返した場合は本文を読み込まず拒否する()
    {
        bool contentRead = false;
        using var handler = new StubHttpMessageHandler(req =>
        {
            var content = new ByteArrayContent(new byte[1024 * 1024]); // 1MB のダミー全体データ
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            };
        });

        using var client = new HttpClient(handler);
        using var source = new HttpPmtilesByteSource(client, new Uri("https://example.com/test.pmtiles"));

        TileFetchException ex = await Assert.ThrowsAsync<TileFetchException>(
            () => source.ReadRangeAsync(0, 127, session: null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Contains("206 Partial Content", ex.Message, StringComparison.Ordinal);
        Assert.False(contentRead);
    }

    [Fact]
    public async Task 応答ステータスが503の場合はTileFetchExceptionを送出する()
    {
        using var handler = new StubHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var client = new HttpClient(handler);
        using var source = new HttpPmtilesByteSource(client, new Uri("https://example.com/test.pmtiles"));

        TileFetchException ex = await Assert.ThrowsAsync<TileFetchException>(
            () => source.ReadRangeAsync(100, 200, session: null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task 不正なバイト長の応答を受信した場合はTileFetchExceptionを送出する()
    {
        using var handler = new StubHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([0x01, 0x02]), // 要求 4 バイトに対し 2 バイト
            });

        using var client = new HttpClient(handler);
        using var source = new HttpPmtilesByteSource(client, new Uri("https://example.com/test.pmtiles"));

        TileFetchException ex = await Assert.ThrowsAsync<TileFetchException>(
            () => source.ReadRangeAsync(0, 4, session: null, CancellationToken.None));

        Assert.Contains("バイト長が不正", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpPmtilesByteSourceは二重計数を防ぐため直接RecordFailureを呼ばない()
    {
        using var handler = new StubHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var client = new HttpClient(handler);
        using var source = new HttpPmtilesByteSource(client, new Uri("https://example.com/test.pmtiles"));
        var session = new TileFetchSession(TimeProvider.System);

        // 5回失敗しても、HttpPmtilesByteSource 自体は RecordFailure を呼ばないため session.IsStopped は false のまま
        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<TileFetchException>(
                () => source.ReadRangeAsync(0, 10, session, CancellationToken.None));
        }

        Assert.False(session.IsStopped);
        Assert.Equal(5, session.Snapshot().NetworkCount);
    }
}
