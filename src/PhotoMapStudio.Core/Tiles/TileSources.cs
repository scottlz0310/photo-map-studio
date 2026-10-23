using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// 既定で選択できるタイルソースのプリセット（NFR-NET-01）。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1054:URI パラメーターは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
public static class TileSources
{
    private const string GsiAttribution = "国土地理院（https://maps.gsi.go.jp/development/ichiran.html）";
    private static readonly Uri GsiAttributionUri = new("https://maps.gsi.go.jp/development/ichiran.html");
    private static readonly Uri GsiOptimalVectorAttributionUri = new("https://github.com/gsi-cyberjapan/optimal_bvmap");
    private static readonly Uri OpenStreetMapAttributionUri = new("https://www.openstreetmap.org/copyright");
    private static readonly Uri OpenFreeMapAttributionUri = new("https://openfreemap.org");

    /// <summary>地理院タイル（淡色）。日本国内のみ。</summary>
    public static TileSource GsiPale { get; } = new(
        "地理院タイル（淡色）",
        "https://cyberjapandata.gsi.go.jp/xyz/pale/{z}/{x}/{y}.png",
        minZoom: 5,
        maxZoom: 18,
        GsiAttribution,
        TileRateLimit.Relaxed,
        GsiAttributionUri);

    /// <summary>地理院タイル（標準）。日本国内のみ。</summary>
    public static TileSource GsiStandard { get; } = new(
        "地理院タイル（標準）",
        "https://cyberjapandata.gsi.go.jp/xyz/std/{z}/{x}/{y}.png",
        minZoom: 5,
        maxZoom: 18,
        GsiAttribution,
        TileRateLimit.Relaxed,
        GsiAttributionUri);

    /// <summary>地理院タイル（最適化ベクトルタイル）。Core 内蔵描画エンジンにより直接描画。</summary>
    public static TileSource GsiVector { get; } = new(
        "地理院タイル（最適化ベクトル）",
        "https://cyberjapandata.gsi.go.jp/xyz/optimal_bvmap-v1/{z}/{x}/{y}.pbf",
        minZoom: 4,
        maxZoom: 18,
        "地理院タイル（最適化ベクトルタイル）",
        TileRateLimit.Relaxed,
        GsiOptimalVectorAttributionUri,
        isVector: true);

    /// <summary>OpenFreeMap ベクタータイル。Core 内蔵描画エンジンにより直接描画。</summary>
    public static TileSource OpenFreeMap { get; } = new(
        "OpenFreeMap（ベクター）",
        "https://tiles.openfreemap.org/planet/{z}/{x}/{y}.pbf",
        minZoom: 0,
        maxZoom: 19,
        "© OpenStreetMap contributors, OpenFreeMap",
        TileRateLimit.Relaxed,
        OpenFreeMapAttributionUri,
        isVector: true);

    /// <summary>OpenStreetMap。</summary>
    public static TileSource OpenStreetMap { get; } = new(
        "OpenStreetMap",
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        minZoom: 0,
        maxZoom: 19,
        "© OpenStreetMap contributors",
        TileRateLimit.OpenStreetMap,
        OpenStreetMapAttributionUri);

    /// <summary>プリセット一覧（表示順）。</summary>
    public static IReadOnlyList<TileSource> All { get; } = [GsiPale, GsiStandard, GsiVector, OpenFreeMap, OpenStreetMap];

    /// <summary>
    /// 既定のタイルソース。
    /// </summary>
    /// <remarks>
    /// 実測（#8）で、一括生成の所要時間・山間部の可読性・ピンの視認性のいずれも淡色が優位だった。
    /// OSM は一括生成が Tile Usage Policy の bulk downloading に該当するため既定にしない。
    /// </remarks>
    public static TileSource Default => GsiPale;

    /// <summary>
    /// 日本国外など、既定のタイルソースが配信していない領域を補う全世界対応のソース。
    /// </summary>
    public static TileSource WorldwideFallback => OpenStreetMap;

    /// <summary>
    /// 任意のタイル URL からタイルソースを構築する（FR-03）。
    /// </summary>
    /// <param name="urlTemplate">URL テンプレート。</param>
    /// <param name="attribution">出典表示。</param>
    /// <param name="minZoom">利用可能な最小ズーム。</param>
    /// <param name="maxZoom">利用可能な最大ズーム。</param>
    /// <returns>構築したタイルソース。ループバックの配信元は <see cref="TileRateLimit.Loopback"/>、それ以外は <see cref="TileRateLimit.Conservative"/>。</returns>
    public static TileSource Custom(string urlTemplate, string attribution, int minZoom = 1, int maxZoom = 19)
        => new(
            "カスタム",
            urlTemplate,
            minZoom,
            maxZoom,
            attribution,
            TileSource.IsLoopbackUrlTemplate(urlTemplate) ? TileRateLimit.Loopback : TileRateLimit.Conservative);

    /// <summary>
    /// 利用者が指定したベクタータイル（PMTiles または <c>{z}/{x}/{y}</c> の URL）からタイルソースを構築する（BYO）。
    /// </summary>
    /// <param name="source">利用者が指定した取得元。</param>
    /// <param name="attribution">出典表示。</param>
    /// <param name="minZoom">利用可能な最小ズーム。</param>
    /// <param name="maxZoom">利用可能な最大ズーム。</param>
    /// <returns>構築したタイルソース。取得元の種別に応じてレート制御が決まる。</returns>
    public static TileSource CustomVector(Vector.VectorSourceSpec source, string attribution, int minZoom = 1, int maxZoom = 19)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new(
            "カスタム（ベクター）",
            source.UrlTemplate,
            minZoom,
            maxZoom,
            attribution,
            source.RateLimit,
            isVector: true)
        {
            VectorSource = source,
        };
    }
}
