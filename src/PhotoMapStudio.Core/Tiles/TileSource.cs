using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイル配信元。URL テンプレート・ズーム範囲・attribution・レート制御方針を束ねる（NFR-NET-01）。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1054:URI パラメーターは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
[SuppressMessage(
    "Design",
    "CA1056:URI プロパティは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
public sealed record TileSource
{
    private static readonly string[] RequiredPlaceholders = ["{z}", "{x}", "{y}"];

    /// <summary>
    /// タイルソースを構築する。
    /// </summary>
    /// <param name="name">表示名。</param>
    /// <param name="urlTemplate"><c>{z}</c> / <c>{x}</c> / <c>{y}</c> を含む URL テンプレート。</param>
    /// <param name="minZoom">利用可能な最小ズーム。</param>
    /// <param name="maxZoom">利用可能な最大ズーム。</param>
    /// <param name="attribution">出典表示（FR-08 で生成画像に焼き込む）。</param>
    /// <param name="rateLimit">レート制御方針。</param>
    public TileSource(
        string name,
        string urlTemplate,
        int minZoom,
        int maxZoom,
        string attribution,
        TileRateLimit rateLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribution);
        ArgumentOutOfRangeException.ThrowIfNegative(minZoom);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxZoom, minZoom);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimit.MaxConcurrentRequests);
        ArgumentOutOfRangeException.ThrowIfNegative(rateLimit.MinimumInterval.Ticks);
        ValidateUrlTemplate(urlTemplate);

        this.Name = name;
        this.UrlTemplate = urlTemplate;
        this.MinZoom = minZoom;
        this.MaxZoom = maxZoom;
        this.Attribution = attribution;
        this.RateLimit = rateLimit;
    }

    /// <summary>表示名。</summary>
    public string Name { get; }

    /// <summary>URL テンプレート。キャッシュキーの算出対象でもある。</summary>
    public string UrlTemplate { get; }

    /// <summary>利用可能な最小ズーム。</summary>
    public int MinZoom { get; }

    /// <summary>利用可能な最大ズーム。</summary>
    public int MaxZoom { get; }

    /// <summary>出典表示。</summary>
    public string Attribution { get; }

    /// <summary>レート制御方針。</summary>
    public TileRateLimit RateLimit { get; }

    /// <summary>指定ズームが利用可能かどうかを返す。</summary>
    /// <param name="zoom">ズームレベル。</param>
    /// <returns>利用可能なら <see langword="true"/>。</returns>
    public bool SupportsZoom(int zoom) => zoom >= this.MinZoom && zoom <= this.MaxZoom;

    /// <summary>
    /// テンプレートのプレースホルダーを置換してタイルの URL を組み立てる。
    /// </summary>
    /// <param name="zoom">ズームレベル。</param>
    /// <param name="x">タイル番号 X。</param>
    /// <param name="y">タイル番号 Y。</param>
    /// <returns>タイルの URL。</returns>
    public Uri BuildTileUri(int zoom, int x, int y) => new(Substitute(this.UrlTemplate, zoom, x, y), UriKind.Absolute);

    private static string Substitute(string template, int zoom, int x, int y) => template
        .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{y}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    // 任意 URL（FR-03）を受け付けるため、ローカルファイル等を指す URL を構築時に弾く
    private static void ValidateUrlTemplate(string urlTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlTemplate);

        foreach (string placeholder in RequiredPlaceholders)
        {
            if (!urlTemplate.Contains(placeholder, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"URL テンプレートに {placeholder} が含まれていません: {urlTemplate}",
                    nameof(urlTemplate));
            }
        }

        if (!Uri.TryCreate(Substitute(urlTemplate, 0, 0, 0), UriKind.Absolute, out Uri? sample)
            || (sample.Scheme != Uri.UriSchemeHttp && sample.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                $"URL テンプレートは http または https の絶対 URL である必要があります: {urlTemplate}",
                nameof(urlTemplate));
        }
    }
}
