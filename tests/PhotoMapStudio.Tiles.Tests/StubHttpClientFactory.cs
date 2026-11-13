using System.Net;

namespace PhotoMapStudio.Tiles.Tests;

/// <summary>
/// ネットワークへ出ずに <see cref="HttpClient"/> を供給するテスト用ファクトリ。
/// </summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly StubHandler handler;

    public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => this.handler = new StubHandler(respond);

    public IReadOnlyList<HttpRequestMessage> Requests => this.handler.Requests;

    /// <summary>作成するクライアントへの設定（アプリが <c>AddHttpClient</c> で行う設定の再現）。</summary>
    public Action<HttpClient>? ConfigureClient { get; set; }

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

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(this.handler, disposeHandler: false);
        this.ConfigureClient?.Invoke(client);
        return client;
    }

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
