using PhotoMapStudio.Tiles;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// 表示範囲全体を 1 枚の画像として描く要求（<see cref="VectorMapRenderer.RenderViewAsync"/>）。
/// </summary>
/// <remarks>
/// <see cref="Zoom"/> は 256px タイルを物理ピクセルで数えた連続ズーム、画像の寸法は物理ピクセルで指定する。
/// 画素密度の高い表示（例: 200% DPI）では、呼び出し側が表示の物理ピクセルに合わせた寸法とズームを渡し、
/// 密度を <see cref="PixelRatio"/> で伝える。伝えないと、文字・線幅など物理ピクセルで決まるスタイルの大きさが、
/// 画素密度が高いほど、画面上（論理ピクセル）では小さくなる。
/// </remarks>
/// <param name="CenterLatitude">画像の中心の緯度（北緯が正）。</param>
/// <param name="CenterLongitude">画像の中心の経度（東経が正）。</param>
/// <param name="Zoom">連続ズーム（0 以上の有限値）。取得するタイルのズームは整数（切り捨て）で、表示倍率は連続値。</param>
/// <param name="PixelWidth">画像の幅（物理ピクセル）。</param>
/// <param name="PixelHeight">画像の高さ（物理ピクセル）。</param>
public sealed record VectorViewRequest(double CenterLatitude, double CenterLongitude, double Zoom, int PixelWidth, int PixelHeight)
{
    /// <summary><see cref="PixelRatio"/> に指定できる最小の値（1/16）。</summary>
    public const double MinPixelRatio = 0.0625;

    /// <summary><see cref="PixelRatio"/> に指定できる最大の値（16。500% DPI でも 5.0）。</summary>
    public const double MaxPixelRatio = 16.0;

    /// <summary>
    /// 表示の画素密度（物理ピクセル ÷ 論理ピクセル。例: 150% DPI は 1.5）。
    /// <see cref="MinPixelRatio"/> 以上 <see cref="MaxPixelRatio"/> 以下で、既定は 1.0（範囲の外は <see cref="ArgumentOutOfRangeException"/>）。
    /// <see cref="Zoom"/> と画像の寸法は、これまでどおり物理ピクセルで指定する。
    /// </summary>
    /// <remarks>
    /// 文字・縁取り・記号・線幅などスタイルの大きさと、スタイルが判定に使うズームを、論理ピクセル
    /// （<see cref="Zoom"/> から log2(<see cref="PixelRatio"/>) を引いた論理ズーム、画像寸法を <see cref="PixelRatio"/> で割った論理寸法）で扱い、
    /// キャンバスをこの倍率に拡大して描く（MapLibre の pixelRatio と同じ考え方）。このため、これらの大きさは論理ピクセルでは
    /// 画素密度によらず一定で、物理ピクセルでは <see cref="PixelRatio"/> 倍になる。地理的な位置・縮尺は変わらず、
    /// 画像の鮮明さは物理ピクセルのまま保たれる。<see cref="PixelRatio"/> が 1.0 のときは、指定しない場合と画素単位で同じ画像になる。
    /// 取得するタイルのズームと範囲は、これまでどおり <see cref="Zoom"/>・画像寸法（物理ピクセル）から決める（取得するタイルの集合は <see cref="PixelRatio"/> に依らない）。
    /// </remarks>
    public double PixelRatio { get; init; } = 1.0;

    /// <summary>描画対象のタイル配信元。リゾルバーを持つレンダラーが、配信元に応じて取得元とスタイルを選ぶ。</summary>
    public TileSource? TileSource { get; init; }

    /// <summary>一括生成単位の取得統計・レート管理セッション。</summary>
    public TileFetchSession? Session { get; init; }

    /// <summary>
    /// 一時的な通信失敗や、通信を止めている間のキャッシュ未命中のタイルを欠落として許し、取得できたタイルだけで描く。
    /// 欠落は <see cref="VectorViewResult"/> の集計で判別できる。既定では許さず、失敗は例外になる。
    /// </summary>
    public bool AllowIncompleteMaps { get; init; }

    /// <summary>
    /// 面・線を、画像を横に分けた帯ごとに並列で描く帯の数（1 で並列にしない。既定は 1）。
    /// 最良の帯の数は実行環境のコア数に近く、コア数より多いと遅くなり得る（目安: <c>Math.Clamp(Environment.ProcessorCount, 1, 8)</c>）。
    /// 帯ごとに別のサーフェスへ描くため、線の縁の半透明画素が、並列にしない場合と最大 20 程度（/255）ずれ得る（画素の数 %。見た目は同じ）。
    /// </summary>
    public int BandCount { get; init; } = 1;

    /// <summary>
    /// 注記（シンボル・道路名・地名）を、面・線の描画と並行して別スレッドの透明な画像へ描き、最後に重ねる。
    /// 注記の衝突判定は逐次のため 1 スレッドで行う。画像を重ねるため、注記の縁の半透明画素が、並列にしない場合とわずかにずれ得る。
    /// </summary>
    public bool ParallelLabels { get; init; }

    /// <summary>
    /// 画像の色形式。既定（<see langword="null"/>）は、実行環境の形式（Windows は BGRA）で、画面へ描く画像に向く。
    /// 別の色形式の画像へ重ねて合成するときは、その形式（例: RGBA）を指定すると、色形式の違いによる半透明画素の丸めのずれが出ない。
    /// </summary>
    public SKColorType? ColorType { get; init; }
}
