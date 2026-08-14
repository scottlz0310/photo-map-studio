using System.Net;

namespace PhotoMapStudio.Core.Tests.Tiles;

/// <summary>
/// ネットワークへ出ずに <see cref="HttpClient"/> を供給するテスト用ファクトリ。
/// </summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly StubHandler handler;

    public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => this.handler = new StubHandler(respond);

    public IReadOnlyList<HttpRequestMessage> Requests => this.handler.Requests;

    public static StubHttpClientFactory WithStatus(HttpStatusCode statusCode, byte[]? content = null)
        => new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(content ?? []),
        });

    /// <summary>ヘッダーは返すが本文の読み取りで失敗する応答を返す。</summary>
    /// <param name="failure">本文の読み取り時に発生させる例外。</param>
    /// <returns>スタブファクトリ。</returns>
    public static StubHttpClientFactory WithFailingBody(Exception failure)
        => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new FailingContent(failure),
        });

    public HttpClient CreateClient(string name) => new(this.handler, disposeHandler: false);

    public void Dispose() => this.handler.Dispose();

    private sealed class FailingContent(Exception failure) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.FromException(failure);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> requests = [];

        public IReadOnlyList<HttpRequestMessage> Requests => this.requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
