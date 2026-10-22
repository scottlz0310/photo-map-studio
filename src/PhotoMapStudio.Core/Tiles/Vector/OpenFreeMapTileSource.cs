using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// OpenFreeMap のパブリックベクタータイル配信（MVT / PBF）からタイルを取得する <see cref="IVectorTileSource"/> 実装。
/// </summary>
public sealed class OpenFreeMapTileSource : IVectorTileSource, IDisposable
{
    /// <summary>TileJSON メタデータの既定 URL。</summary>
    public const string DefaultTileJsonUrl = "https://tiles.openfreemap.org/planet";

    /// <summary>TileJSON 取得失敗時に使用するフォールバックの URL テンプレート。</summary>
    public const string FallbackUrlTemplate = "https://tiles.openfreemap.org/planet/20261018_080001_pt/{z}/{x}/{y}.pbf";

    /// <summary>このソースのプレフィックス識別子。</summary>
    public const string BaseIdentifier = "openfreemap";

    /// <summary>OpenFreeMap の最大提供ズームレベル。</summary>
    public const int DefaultMaxZoom = 14;

    private readonly HttpClient httpClient;
    private readonly string? explicitUrlTemplate;
    private readonly string tileJsonUrl;
    private readonly SemaphoreSlim resolveLock = new(1, 1);
    private string? resolvedUrlTemplate;

    /// <summary>
    /// OpenFreeMap タイルソースを初期化する。
    /// </summary>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="urlTemplate">明示的に指定する固定 URL テンプレート。省略時は TileJSON から動的に解決する。</param>
    /// <param name="maxZoom">最大ズームレベル。省略時は <see cref="DefaultMaxZoom"/>。</param>
    /// <param name="tileJsonUrl">TileJSON の取得元 URL。省略時は <see cref="DefaultTileJsonUrl"/>。</param>
    [SuppressMessage(
        "Design",
        "CA1054:URI パラメーターは文字列にしません",
        Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
    public OpenFreeMapTileSource(
        HttpClient httpClient,
        string? urlTemplate = null,
        int maxZoom = DefaultMaxZoom,
        string? tileJsonUrl = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.explicitUrlTemplate = string.IsNullOrWhiteSpace(urlTemplate) ? null : urlTemplate;
        this.tileJsonUrl = string.IsNullOrWhiteSpace(tileJsonUrl) ? DefaultTileJsonUrl : tileJsonUrl;
        this.MaxZoom = maxZoom;
        this.resolvedUrlTemplate = this.explicitUrlTemplate;
    }

    /// <inheritdoc />
    public string SourceId => $"{BaseIdentifier}:{this.EffectiveUrlTemplate}";

    /// <inheritdoc />
    public int MaxZoom { get; }

    /// <inheritdoc />
    public async Task<string> GetSourceIdAsync(CancellationToken cancellationToken = default)
    {
        await this.ResolveUrlTemplateAsync(cancellationToken).ConfigureAwait(false);
        return this.SourceId;
    }

    /// <summary>
    /// 現在解決されている実効 URL テンプレート。未解決時はフォールバック URL を返す。
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1056:URI プロパティは文字列にしません",
        Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
    public string EffectiveUrlTemplate => this.resolvedUrlTemplate ?? this.explicitUrlTemplate ?? FallbackUrlTemplate;

    /// <summary>
    /// TileJSON から最新の URL テンプレートを解決する（明示指定時は何もしない）。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>解決された URL テンプレート。</returns>
    [SuppressMessage(
        "Reliability",
        "CA1508:Avoid dead code",
        Justification = "Double-checked locking pattern across await lock.")]
    public async Task<string> ResolveUrlTemplateAsync(CancellationToken cancellationToken = default)
    {
        if (this.explicitUrlTemplate is not null)
        {
            return this.explicitUrlTemplate;
        }

        if (Volatile.Read(ref this.resolvedUrlTemplate) is { } current)
        {
            return current;
        }

        await this.resolveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.resolvedUrlTemplate is not null)
            {
                return this.resolvedUrlTemplate;
            }

            var requestUri = new Uri(this.tileJsonUrl, UriKind.Absolute);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.UserAgent.ParseAdd(UserAgentProvider.Value);

            using HttpResponseMessage response = await this.httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (doc.RootElement.TryGetProperty("tiles", out JsonElement tiles) &&
                    tiles.ValueKind == JsonValueKind.Array &&
                    tiles.GetArrayLength() > 0)
                {
                    string? template = tiles[0].GetString();
                    if (!string.IsNullOrWhiteSpace(template))
                    {
                        this.resolvedUrlTemplate = template;
                        return template;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 呼び出し元の明示的なキャンセル要求は状態を確定せず上位へ伝播させる
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            // オフラインや一時エラー、内部タイムアウト時はフォールバックテンプレートを使用
        }
        finally
        {
            this.resolveLock.Release();
        }

        this.resolvedUrlTemplate = FallbackUrlTemplate;
        return FallbackUrlTemplate;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken cancellationToken = default)
    {
        string template = await this.ResolveUrlTemplateAsync(cancellationToken).ConfigureAwait(false);

        string requestUrl = template
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

    /// <inheritdoc />
    public void Dispose()
    {
        this.resolveLock.Dispose();
    }
}
