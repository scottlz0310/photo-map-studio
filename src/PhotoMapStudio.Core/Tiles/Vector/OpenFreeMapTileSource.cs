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

    /// <summary>最後に解決した URL テンプレートを保存するキャッシュキー。</summary>
    public const string UrlTemplateStoreKey = "openfreemap-urltemplate.txt";

    /// <summary>TileJSON の取得に失敗した後、次に取得を試みるまでの待機時間。タイルごとの再要求を避ける。</summary>
    public static TimeSpan ResolveRetryInterval { get; } = TimeSpan.FromSeconds(60);

    private readonly HttpClient httpClient;
    private readonly string? explicitUrlTemplate;
    private readonly string tileJsonUrl;
    private readonly SemaphoreSlim resolveLock = new(1, 1);
    private readonly ITileCache? urlTemplateStore;
    private readonly TimeProvider timeProvider;
    private string? resolvedUrlTemplate;
    private string? lastKnownUrlTemplate;
    private bool lastKnownLoaded;
    private DateTimeOffset? lastResolveFailure;

    /// <summary>
    /// OpenFreeMap タイルソースを初期化する。
    /// </summary>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="urlTemplate">明示的に指定する固定 URL テンプレート。省略時は TileJSON から動的に解決する。</param>
    /// <param name="maxZoom">最大ズームレベル。省略時は <see cref="DefaultMaxZoom"/>。</param>
    /// <param name="tileJsonUrl">TileJSON の取得元 URL。省略時は <see cref="DefaultTileJsonUrl"/>。</param>
    /// <param name="urlTemplateStore">最後に解決した URL テンプレートの保存先。指定すると、オフラインなどで TileJSON を取得できないときに前回の版を使い、その版のキャッシュを引き続き利用できる。</param>
    /// <param name="timeProvider">取得失敗後の待機時間の判定に使う時刻源。</param>
    [SuppressMessage(
        "Design",
        "CA1054:URI パラメーターは文字列にしません",
        Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
    public OpenFreeMapTileSource(
        HttpClient httpClient,
        string? urlTemplate = null,
        int maxZoom = DefaultMaxZoom,
        string? tileJsonUrl = null,
        ITileCache? urlTemplateStore = null,
        TimeProvider? timeProvider = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.explicitUrlTemplate = string.IsNullOrWhiteSpace(urlTemplate) ? null : urlTemplate;
        this.tileJsonUrl = string.IsNullOrWhiteSpace(tileJsonUrl) ? DefaultTileJsonUrl : tileJsonUrl;
        this.MaxZoom = maxZoom;
        this.resolvedUrlTemplate = this.explicitUrlTemplate;
        this.urlTemplateStore = urlTemplateStore;
        this.timeProvider = timeProvider ?? TimeProvider.System;
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
    /// 現在の実効 URL テンプレート。未解決時は前回解決した URL、それも無ければフォールバック URL を返す。
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1056:URI プロパティは文字列にしません",
        Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
    public string EffectiveUrlTemplate
        => this.resolvedUrlTemplate ?? this.explicitUrlTemplate ?? this.lastKnownUrlTemplate ?? FallbackUrlTemplate;

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

            await this.LoadLastKnownUrlTemplateAsync(cancellationToken).ConfigureAwait(false);

            // 取得に失敗した直後は、タイルごとに TileJSON を再要求しないよう待機する
            if (this.lastResolveFailure is { } failedAt
                && this.timeProvider.GetUtcNow() - failedAt < ResolveRetryInterval)
            {
                return this.EffectiveUrlTemplate;
            }

            string? template = await this.TryFetchUrlTemplateAsync(cancellationToken).ConfigureAwait(false);
            if (template is not null)
            {
                this.resolvedUrlTemplate = template;
                this.lastResolveFailure = null;
                await this.SaveLastKnownUrlTemplateAsync(template, cancellationToken).ConfigureAwait(false);
                return template;
            }

            // 失敗は確定させない。次回（待機後）に TileJSON を再取得して最新の版へ戻れるようにする
            this.lastResolveFailure = this.timeProvider.GetUtcNow();
            return this.EffectiveUrlTemplate;
        }
        finally
        {
            this.resolveLock.Release();
        }
    }

    private async Task<string?> TryFetchUrlTemplateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = new Uri(this.tileJsonUrl, UriKind.Absolute);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.UserAgent.ParseAdd(UserAgentProvider.Value);

            using HttpResponseMessage response = await this.httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("tiles", out JsonElement tiles) &&
                tiles.ValueKind == JsonValueKind.Array &&
                tiles.GetArrayLength() > 0)
            {
                string? template = tiles[0].GetString();
                return string.IsNullOrWhiteSpace(template) ? null : template;
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 呼び出し元の明示的なキャンセル要求は状態を確定せず上位へ伝播させる
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            // オフラインや一時エラー、内部タイムアウト時は前回の版（なければフォールバック）を使う
            return null;
        }
    }

    private async Task LoadLastKnownUrlTemplateAsync(CancellationToken cancellationToken)
    {
        if (this.lastKnownLoaded || this.urlTemplateStore is null)
        {
            return;
        }

        byte[]? stored = await this.urlTemplateStore.TryReadAsync(UrlTemplateStoreKey, cancellationToken).ConfigureAwait(false);
        if (stored is { Length: > 0 })
        {
            string template = System.Text.Encoding.UTF8.GetString(stored).Trim();

            // 破損や改ざんで想定外の文字列を要求先にしない
            if (IsValidUrlTemplate(template))
            {
                this.lastKnownUrlTemplate = template;
            }
        }

        // 読み込みが完了してから確定する。呼び出し元のキャンセルで中断された場合は、次回また読み直す
        this.lastKnownLoaded = true;
    }

    /// <summary>https かつ座標のプレースホルダーを持つものだけを有効な URL テンプレートとする。</summary>
    private static bool IsValidUrlTemplate(string template)
        => template.StartsWith("https://", StringComparison.Ordinal)
            && template.Contains("{z}", StringComparison.Ordinal)
            && template.Contains("{x}", StringComparison.Ordinal)
            && template.Contains("{y}", StringComparison.Ordinal);

    private async Task SaveLastKnownUrlTemplateAsync(string template, CancellationToken cancellationToken)
    {
        if (this.urlTemplateStore is null)
        {
            return;
        }

        try
        {
            await this.urlTemplateStore
                .WriteAsync(UrlTemplateStoreKey, System.Text.Encoding.UTF8.GetBytes(template), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存に失敗しても今回の解決結果はそのまま使う（次回オフライン時に前回の版を使えないだけ）
        }
    }

    /// <inheritdoc />
    public Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken cancellationToken = default)
        => this.GetTileAsync(z, x, y, session: null, cancellationToken);

    /// <inheritdoc />
    public async Task<byte[]?> GetTileAsync(
        int z,
        int x,
        int y,
        TileFetchSession? session,
        CancellationToken cancellationToken = default)
    {
        string template = await this.ResolveUrlTemplateAsync(cancellationToken).ConfigureAwait(false);
        return await this.FetchTileAsync(template, z, x, y, session, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetTileForSourceIdAsync(
        int z,
        int x,
        int y,
        string resolvedSourceId,
        TileFetchSession? session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolvedSourceId);

        // 識別子（= キャッシュ鍵の元）に含まれる版の URL で取得する。再解決で版が変わっていても、
        // 取得したタイルを別の版の鍵で保存しない。
        string? template = this.TemplateFromSourceId(resolvedSourceId);
        template ??= await this.ResolveUrlTemplateAsync(cancellationToken).ConfigureAwait(false);
        return await this.FetchTileAsync(template, z, x, y, session, cancellationToken).ConfigureAwait(false);
    }

    private string? TemplateFromSourceId(string sourceId)
    {
        // 明示指定のテンプレートは版が変わらない
        if (this.explicitUrlTemplate is not null)
        {
            return this.explicitUrlTemplate;
        }

        string prefix = BaseIdentifier + ":";
        if (!sourceId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string template = sourceId[prefix.Length..];
        return IsValidUrlTemplate(template) ? template : null;
    }

    private async Task<byte[]?> FetchTileAsync(
        string template,
        int z,
        int x,
        int y,
        TileFetchSession? session,
        CancellationToken cancellationToken)
    {
        session?.ThrowIfStopped();

        string requestUrl = template
            .Replace("{z}", z.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var requestUri = new Uri(requestUrl, UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(UserAgentProvider.Value);

        try
        {
            session?.RecordRequest();
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
