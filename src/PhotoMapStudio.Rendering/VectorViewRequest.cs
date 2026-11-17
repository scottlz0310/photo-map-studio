using PhotoMapStudio.Tiles;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// 表示範囲全体を 1 枚の画像として描く要求（<see cref="VectorMapRenderer.RenderViewAsync"/>）。
/// </summary>
/// <remarks>
/// <see cref="Zoom"/> は 256px タイルを物理ピクセルで数えた連続ズーム、画像の寸法は物理ピクセルで指定する。
/// 画素密度の高い表示（例: 200% DPI）では、呼び出し側が表示の物理ピクセルに合わせた寸法とズームを渡す
/// （ウィンドウの DPI との丸めの差を避けるため、描画側は密度を持たない）。
/// </remarks>
/// <param name="CenterLatitude">画像の中心の緯度（北緯が正）。</param>
/// <param name="CenterLongitude">画像の中心の経度（東経が正）。</param>
/// <param name="Zoom">連続ズーム（0 以上の有限値）。取得するタイルのズームは整数（切り捨て）で、表示倍率は連続値。</param>
/// <param name="PixelWidth">画像の幅（物理ピクセル）。</param>
/// <param name="PixelHeight">画像の高さ（物理ピクセル）。</param>
public sealed record VectorViewRequest(double CenterLatitude, double CenterLongitude, double Zoom, int PixelWidth, int PixelHeight)
{
    /// <summary>描画対象のタイル配信元。リゾルバーを持つレンダラーが、配信元に応じて取得元とスタイルを選ぶ。</summary>
    public TileSource? TileSource { get; init; }

    /// <summary>一括生成単位の取得統計・レート管理セッション。</summary>
    public TileFetchSession? Session { get; init; }

    /// <summary>
    /// 一時的な通信失敗や、通信を止めている間のキャッシュ未命中のタイルを欠落として許し、取得できたタイルだけで描く。
    /// 欠落は <see cref="VectorViewResult"/> の集計で判別できる。既定では許さず、失敗は例外になる。
    /// </summary>
    public bool AllowIncompleteMaps { get; init; }
}
