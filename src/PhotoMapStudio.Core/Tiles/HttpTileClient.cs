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

        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"タイルの取得が HTTP {(int)response.StatusCode} で失敗しました: {requestUri}");
                throw new TileFetchException(message, requestUri, response.StatusCode, null);
            }

            // ResponseHeadersRead のため本文の受信はここで行われる。切断も取得失敗として扱う
            // 再エンコードせずレスポンスのバイト列をそのまま返す（仕様書 §5.3-3）
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // 呼び出し元のキャンセルは伝播させ、HttpClient のタイムアウトだけを取得失敗に変換する
            throw new TileFetchException($"タイルの取得がタイムアウトしました: {requestUri}", requestUri, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TileFetchException($"タイルを取得できませんでした: {requestUri}", requestUri, null, ex);
        }
        catch (IOException ex)
        {
            throw new TileFetchException($"タイルの本文を読み取れませんでした: {requestUri}", requestUri, null, ex);
        }
    }
}
