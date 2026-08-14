using System.Globalization;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// <see cref="HttpClient"/> によるタイル取得。
/// </summary>
public sealed class HttpTileClient : ITileClient
{
    /// <summary><see cref="IHttpClientFactory"/> から取得するクライアントの名前。</summary>
    public const string HttpClientName = "PhotoMapStudio.Tiles";

    private readonly IHttpClientFactory httpClientFactory;

    /// <summary>
    /// クライアントを初期化する。
    /// </summary>
    /// <param name="httpClientFactory"><see cref="HttpClient"/> の生成元。</param>
    public HttpTileClient(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        this.httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public async Task<byte[]> GetTileAsync(
        TileSource source,
        int zoom,
        int x,
        int y,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        Uri requestUri = source.BuildTileUri(zoom, x, y);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        // ライブラリ既定の User-Agent は OSM でブロック対象のため、必ず自前の値を表明する
        request.Headers.UserAgent.ParseAdd(UserAgentProvider.Value);

        HttpClient client = this.httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new TileFetchException($"タイルを取得できませんでした: {requestUri}", requestUri, null, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"タイルの取得が HTTP {(int)response.StatusCode} で失敗しました: {requestUri}");
                throw new TileFetchException(message, requestUri, response.StatusCode, null);
            }

            // 再エンコードせずレスポンスのバイト列をそのまま返す（仕様書 §5.3-3）
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
