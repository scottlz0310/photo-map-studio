using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// OpenFreeMap のパブリックベクタータイル配信（MVT / PBF）からタイルを取得する <see cref="IVectorTileSource"/> 実装。
/// </summary>
public sealed class OpenFreeMapTileSource : IVectorTileSource
{
    /// <summary>OpenFreeMap の既定のタイル URL テンプレート。</summary>
    public const string DefaultUrlTemplate = "https://tiles.openfreemap.org/planet/20261018_080001_pt/{z}/{x}/{y}.pbf";

    /// <summary>このソースの一意な識別子。</summary>
    public const string Identifier = "openfreemap";

    /// <summary>OpenFreeMap の最大提供ズームレベル。</summary>
    public const int DefaultMaxZoom = 14;

    private readonly HttpClient httpClient;
    private readonly string urlTemplate;

    /// <summary>
    /// OpenFreeMap タイルソースを初期化する。
    /// </summary>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="urlTemplate">URL テンプレート。省略時は <see cref="DefaultUrlTemplate"/>。</param>
    /// <param name="maxZoom">最大ズームレベル。省略時は <see cref="DefaultMaxZoom"/>。</param>
    [SuppressMessage(
        "Design",
        "CA1054:URI パラメーターは文字列にしません",
        Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
    public OpenFreeMapTileSource(HttpClient httpClient, string? urlTemplate = null, int maxZoom = DefaultMaxZoom)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.urlTemplate = string.IsNullOrWhiteSpace(urlTemplate) ? DefaultUrlTemplate : urlTemplate;
        this.MaxZoom = maxZoom;
    }

    /// <inheritdoc />
    public string SourceId => Identifier;

    /// <inheritdoc />
    public int MaxZoom { get; }

    /// <inheritdoc />
    public async Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken cancellationToken = default)
    {
        string requestUrl = this.urlTemplate
            .Replace("{z}", z.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var requestUri = new Uri(requestUrl, UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(UserAgentProvider.Value);

        try
        {
            using HttpResponseMessage response = await this.httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // データが存在しない領域・ズームは null を返す
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                string message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"OpenFreeMap ベクタータイルの取得が HTTP {(int)response.StatusCode} で失敗しました: {requestUri}");
                throw new TileFetchException(message, requestUri, response.StatusCode, null);
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return bytes.Length == 0 ? null : bytes;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TileFetchException($"OpenFreeMap ベクタータイルの取得がタイムアウトしました: {requestUri}", requestUri, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TileFetchException($"OpenFreeMap ベクタータイルを取得できませんでした: {requestUri}", requestUri, null, ex);
        }
    }
}
