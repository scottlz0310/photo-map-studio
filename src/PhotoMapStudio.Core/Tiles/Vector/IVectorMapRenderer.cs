using PhotoMapStudio.Core.Geo;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// ベクタータイルから SkiaSharp キャンバスへ地図を描画するレンダラーの契約。
/// </summary>
public interface IVectorMapRenderer
{
    /// <summary>
    /// 指定されたキャンバスにベクター地図を直接描画する。
    /// </summary>
    /// <param name="canvas">描画先のキャンバス。</param>
    /// <param name="center">描画領域の中心地理座標。</param>
    /// <param name="zoom">表示ズームレベル。</param>
    /// <param name="width">描画幅（ピクセル）。</param>
    /// <param name="height">描画高さ（ピクセル）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    Task RenderToCanvasAsync(
        SKCanvas canvas,
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        CancellationToken cancellationToken = default)
        => this.RenderToCanvasAsync(canvas, center, zoom, width, height, session: null, cancellationToken);

    /// <summary>
    /// 指定されたキャンバスにベクター地図を直接描画する（セッション統計付き）。
    /// </summary>
    /// <param name="canvas">描画先のキャンバス。</param>
    /// <param name="center">描画領域の中心地理座標。</param>
    /// <param name="zoom">表示ズームレベル。</param>
    /// <param name="width">描画幅（ピクセル）。</param>
    /// <param name="height">描画高さ（ピクセル）。</param>
    /// <param name="session">一括生成単位の取得統計・レート管理セッション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    Task RenderToCanvasAsync(
        SKCanvas canvas,
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        TileFetchSession? session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定された地理座標、ズームレベル、出力サイズでベクター地図をラスタライズして <see cref="SKBitmap"/> を生成する。
    /// </summary>
    /// <param name="center">描画領域の中心地理座標。</param>
    /// <param name="zoom">表示ズームレベル。</param>
    /// <param name="width">出力画像の幅（ピクセル）。</param>
    /// <param name="height">出力画像の高さ（ピクセル）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>描画されたビットマップ画像。</returns>
    Task<SKBitmap> RenderMapAsync(
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        CancellationToken cancellationToken = default)
        => this.RenderMapAsync(center, zoom, width, height, session: null, cancellationToken);

    /// <summary>
    /// 指定された地理座標、ズームレベル、出力サイズでベクター地図をラスタライズして <see cref="SKBitmap"/> を生成する（セッション統計付き）。
    /// </summary>
    /// <param name="center">描画領域の中心地理座標。</param>
    /// <param name="zoom">表示ズームレベル。</param>
    /// <param name="width">出力画像の幅（ピクセル）。</param>
    /// <param name="height">出力画像の高さ（ピクセル）。</param>
    /// <param name="session">一括生成単位の取得統計・レート管理セッション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>描画されたビットマップ画像。</returns>
    Task<SKBitmap> RenderMapAsync(
        GeoCoordinate center,
        int zoom,
        int width,
        int height,
        TileFetchSession? session,
        CancellationToken cancellationToken = default);
}
