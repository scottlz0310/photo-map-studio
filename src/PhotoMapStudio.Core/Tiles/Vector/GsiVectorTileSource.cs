using PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 国土地理院の最適化ベクトルタイル（optimal_bvmap-v1.pmtiles）から HTTP Range 取得するベクタータイルソースの構築を提供します。
/// </summary>
public static class GsiVectorTileSource
{
    /// <summary>国土地理院 最適化ベクトルタイル PMTiles の既定 URL。</summary>
    public const string DefaultPmtilesUrl = "https://cyberjapandata.gsi.go.jp/xyz/optimal_bvmap-v1/optimal_bvmap-v1.pmtiles";

    /// <summary>国土地理院 最適化ベクトルタイルのソース識別子。</summary>
    public const string SourceIdentifier = "gsi-optimal-bvmap";

    /// <summary>国土地理院 最適化ベクトルタイルの最大提供ズームレベル (過拡大により z17〜18 描画可能)。</summary>
    public const int DefaultMaxZoom = 16;

    /// <summary>
    /// 国土地理院 最適化ベクトルタイルの PMTiles タイルソースを生成します。
    /// </summary>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="pmtilesUri">PMTiles の URL（省略時は既定 URL）。</param>
    /// <param name="maxConcurrency">国土地理院サーバーへの同時最大リクエスト数 (既定: 2)。</param>
    /// <returns>初期化された <see cref="IVectorTileSource"/>。</returns>
    public static IVectorTileSource Create(
        HttpClient httpClient,
        Uri? pmtilesUri = null,
        int maxConcurrency = 2)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        Uri uri = pmtilesUri ?? new Uri(DefaultPmtilesUrl);
        var byteSource = new HttpPmtilesByteSource(httpClient, uri, maxConcurrency);
        return new PmtilesVectorTileSource(byteSource, SourceIdentifier, fallbackMaxZoom: DefaultMaxZoom);
    }
}
