using PhotoMapStudio.Tiles;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// 描画せずにタイルを先に取得・デコードする要求（<see cref="IVectorMapPrefetcher.PrefetchAsync"/>）。
/// </summary>
/// <param name="CenterLatitude">中心の緯度（北緯が正）。</param>
/// <param name="CenterLongitude">中心の経度（東経が正）。</param>
/// <param name="Zoom">連続ズーム（0 以上の有限値）。<see cref="VectorViewRequest.Zoom"/> と同じ意味で、取得するタイルのズームは整数（切り捨て）。</param>
public sealed record VectorPrefetchRequest(double CenterLatitude, double CenterLongitude, double Zoom)
{
    /// <summary>対象のタイル配信元。リゾルバーを持つレンダラーが、配信元に応じて取得元を選ぶ。</summary>
    public TileSource? TileSource { get; init; }

    /// <summary>一括生成単位の取得統計・レート管理セッション。</summary>
    public TileFetchSession? Session { get; init; }
}
