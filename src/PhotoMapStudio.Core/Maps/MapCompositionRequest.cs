using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// 地図画像の合成条件。
/// </summary>
public sealed record MapCompositionRequest
{
    /// <summary>既定の出力幅。</summary>
    public const int DefaultWidth = 800;

    /// <summary>既定の出力高さ。</summary>
    public const int DefaultHeight = 600;

    /// <summary>既定のズームレベル。</summary>
    public const int DefaultZoom = 15;

    /// <summary>出力画像の中心となる撮影地点。</summary>
    public required GeoCoordinate Center { get; init; }

    /// <summary>使用するタイルソース。</summary>
    public TileSource TileSource { get; init; } = TileSources.Default;

    /// <summary>出力画像の幅（ピクセル）。</summary>
    public int Width { get; init; } = DefaultWidth;

    /// <summary>出力画像の高さ（ピクセル）。</summary>
    public int Height { get; init; } = DefaultHeight;

    /// <summary>ズームレベル。</summary>
    public int Zoom { get; init; } = DefaultZoom;

    /// <summary>ピン画像のパス。未指定・不存在・読み込み失敗の場合は代替描画に切り替える。</summary>
    public string? PinImagePath { get; init; }

    /// <summary>
    /// タイルソースの配信範囲外だった場合に、全世界対応のソースへ切り替えるかどうか。
    /// </summary>
    /// <remarks>
    /// 既定は <see langword="true"/>（プレビューと単発生成）。一括生成は代替ソース
    /// （OpenStreetMap）の Tile Usage Policy が禁じる bulk downloading に該当するため、
    /// <see langword="false"/> を指定して切り替えを行わない。
    /// </remarks>
    public bool AllowWorldwideFallback { get; init; } = true;
}
