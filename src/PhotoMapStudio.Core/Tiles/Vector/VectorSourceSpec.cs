using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 利用者が指定するベクタータイルの取得元の種別。
/// </summary>
public enum VectorSourceKind
{
    /// <summary><c>{z}/{x}/{y}</c> を含む URL テンプレート（MVT / PBF）。</summary>
    XyzTemplate,

    /// <summary>HTTP(S) で公開されている PMTiles アーカイブ（HTTP Range 取得）。</summary>
    PmtilesUrl,

    /// <summary>この PC 上の PMTiles ファイル。</summary>
    PmtilesFile,
}

/// <summary>
/// ベクタータイルのレイヤー構成（スキーマ）。描画スタイルの選択に使う。
/// </summary>
public enum VectorStyleSchema
{
    /// <summary>OpenMapTiles スキーマ（OpenStreetMap 系）。</summary>
    OpenMapTiles,

    /// <summary>国土地理院 最適化ベクトルタイル（optimal_bvmap）。</summary>
    GsiOptimalBvmap,
}

/// <summary>
/// 利用者が指定したベクタータイルの取得元（BYO）。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1056:URI プロパティは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
public sealed record VectorSourceSpec
{
    private const string PmtilesExtension = ".pmtiles";

    private VectorSourceSpec(string location, VectorSourceKind kind, VectorStyleSchema schema)
    {
        this.Location = location;
        this.Kind = kind;
        this.Schema = schema;
    }

    /// <summary>URL テンプレート、PMTiles の URL、または PMTiles のファイルパス。</summary>
    public string Location { get; }

    /// <summary>取得元の種別。</summary>
    public VectorSourceKind Kind { get; }

    /// <summary>レイヤー構成（描画スタイルの選択に使う）。</summary>
    public VectorStyleSchema Schema { get; }

    /// <summary>
    /// 取得元のレート制御方針。この PC 内（ファイル・ループバック）は通信しない、または他者に負荷を掛けない。
    /// </summary>
    public TileRateLimit RateLimit => this.Kind == VectorSourceKind.PmtilesFile || TileSource.IsLoopbackUrlTemplate(this.UrlTemplate)
        ? TileRateLimit.Loopback
        : TileRateLimit.Conservative;

    /// <summary>
    /// <see cref="TileSource"/> に持たせる URL テンプレート。配信元のホスト判定（ループバックなど）とキャッシュの識別に使う。
    /// 実際の取得は <see cref="Location"/> から行い、このテンプレートでは要求しない。
    /// </summary>
    public string UrlTemplate => this.Kind switch
    {
        VectorSourceKind.XyzTemplate => this.Location,
        VectorSourceKind.PmtilesUrl => this.Location + "#{z}/{x}/{y}",

        // ファイルは通信しないため、ループバックの仮の URL にする。別のファイルを区別できるよう、パスのハッシュを含める
        _ => string.Create(CultureInfo.InvariantCulture, $"http://localhost/pmtiles-file/{HashOf(this.Location)}#{{z}}/{{x}}/{{y}}"),
    };

    /// <summary>
    /// 利用者の入力から取得元を作る。
    /// </summary>
    /// <param name="location">URL テンプレート、PMTiles の URL、または PMTiles のファイルパス。</param>
    /// <param name="schema">レイヤー構成。</param>
    /// <returns>作成した取得元。</returns>
    /// <exception cref="ArgumentException">入力が不正な場合。</exception>
    public static VectorSourceSpec Create(string? location, VectorStyleSchema schema)
        => TryCreate(location, schema, out VectorSourceSpec? spec, out string error)
            ? spec!
            : throw new ArgumentException(error);

    /// <summary>
    /// 利用者の入力から取得元を作る。入力が不正な場合は理由を返す。
    /// </summary>
    /// <param name="location">URL テンプレート、PMTiles の URL、または PMTiles のファイルパス。</param>
    /// <param name="schema">レイヤー構成。</param>
    /// <param name="spec">作成した取得元。</param>
    /// <param name="error">不正な場合の理由。</param>
    /// <returns>作成できた場合は <see langword="true"/>。</returns>
    public static bool TryCreate(
        string? location,
        VectorStyleSchema schema,
        [NotNullWhen(true)] out VectorSourceSpec? spec,
        out string error)
    {
        spec = null;
        string trimmed = location?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "PMTiles の URL またはファイルパス、あるいは {z}/{x}/{y} を含むベクタータイルの URL を指定してください。";
            return false;
        }

        if (!Enum.IsDefined(schema))
        {
            error = "レイヤー構成（スキーマ）が不正です。";
            return false;
        }

        bool hasPlaceholders = trimmed.Contains("{z}", StringComparison.Ordinal)
            && trimmed.Contains("{x}", StringComparison.Ordinal)
            && trimmed.Contains("{y}", StringComparison.Ordinal);

        if (hasPlaceholders)
        {
            string sample = trimmed
                .Replace("{z}", "0", StringComparison.Ordinal)
                .Replace("{x}", "0", StringComparison.Ordinal)
                .Replace("{y}", "0", StringComparison.Ordinal);
            if (!TryGetHttpUri(sample, out Uri? templateUri, out error) || !CheckUrl(templateUri, allowFragment: true, out error))
            {
                return false;
            }

            spec = new VectorSourceSpec(trimmed, VectorSourceKind.XyzTemplate, schema);
            return true;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && uri.Scheme != Uri.UriSchemeFile)
        {
            if (!TryGetHttpUri(trimmed, out Uri? httpUri, out error) || !CheckUrl(httpUri, allowFragment: false, out error))
            {
                return false;
            }

            if (!httpUri.AbsolutePath.EndsWith(PmtilesExtension, StringComparison.OrdinalIgnoreCase))
            {
                error = "URL は PMTiles（拡張子 .pmtiles）か、{z}/{x}/{y} を含むベクタータイルの URL を指定してください。";
                return false;
            }

            spec = new VectorSourceSpec(trimmed, VectorSourceKind.PmtilesUrl, schema);
            return true;
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            error = "ファイルは絶対パスで指定してください。URL を指定する場合は http または https にしてください。";
            return false;
        }

        if (!trimmed.EndsWith(PmtilesExtension, StringComparison.OrdinalIgnoreCase))
        {
            error = "ファイルは PMTiles（拡張子 .pmtiles）を指定してください。";
            return false;
        }

        if (!File.Exists(trimmed))
        {
            error = $"PMTiles ファイルが見つかりません: {trimmed}";
            return false;
        }

        spec = new VectorSourceSpec(trimmed, VectorSourceKind.PmtilesFile, schema);
        error = string.Empty;
        return true;
    }

    private static bool TryGetHttpUri(string text, [NotNullWhen(true)] out Uri? uri, out string error)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            uri = null;
            error = "URL は http または https の絶対 URL を指定してください。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool CheckUrl(Uri uri, bool allowFragment, out string error)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "URL に認証情報（ユーザー名・パスワード）を含めないでください。";
            return false;
        }

        if (!allowFragment && !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "URL に # 以降（フラグメント）を含めないでください。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string HashOf(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];
}
