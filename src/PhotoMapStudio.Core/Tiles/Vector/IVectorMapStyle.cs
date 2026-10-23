using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// ベクタータイルの描画スタイル（配色・線幅・階層・注記ルール）を規定するインターフェース。
/// </summary>
public interface IVectorMapStyle
{
    /// <summary>スタイルの識別子（例: "openmaptiles", "gsi"）。</summary>
    string StyleId { get; }

    /// <summary>地図キャンバスの背景色。</summary>
    SKColor BackgroundColor { get; }

    /// <summary>
    /// 面（ポリゴン: 水域、緑地、建物等）を描画する。
    /// </summary>
    /// <param name="canvas">描画先 SkiaSharp キャンバス。</param>
    /// <param name="tile">対象ベクタータイルデータ。</param>
    /// <param name="tileScale">タイルのスケール係数（Extent / ピクセル変換用）。</param>
    /// <param name="zoom">現在の表示ズームレベル。</param>
    void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom);

    /// <summary>
    /// 線（ライン: 道路、鉄道、河川等）を描画する。
    /// </summary>
    /// <param name="canvas">描画先 SkiaSharp キャンバス。</param>
    /// <param name="tile">対象ベクタータイルデータ。</param>
    /// <param name="tileScale">タイルのスケール係数。</param>
    /// <param name="zoom">現在の表示ズームレベル。</param>
    void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom);

    /// <summary>
    /// 注記より優先して配置するシンボル（経路番号の盾など）を描画する。
    /// 全タイルのシンボルを先に配置してから注記を配置するため、タイルの描画順に左右されず優先される。
    /// </summary>
    /// <remarks>シンボルを持たないスタイルは、既定の実装（何も描画しない）のままでよい。</remarks>
    /// <param name="canvas">描画先 SkiaSharp キャンバス（画面座標系）。</param>
    /// <param name="tile">対象ベクタータイルデータ。</param>
    /// <param name="tileToCanvasMatrix">タイルローカル座標から画面ピクセル座標への変換行列。</param>
    /// <param name="zoom">現在の表示ズームレベル。</param>
    /// <param name="collisionGrid">ラベル衝突判定グリッド（注記と共有する）。</param>
    /// <param name="viewport">描画画面全体の矩形。</param>
    void RenderSymbols(
        SKCanvas canvas,
        VectorTile tile,
        SKMatrix tileToCanvasMatrix,
        int zoom,
        LabelCollisionGrid collisionGrid,
        SKRect viewport)
    {
    }

    /// <summary>
    /// 注記・ラベル（道路名、地名、施設シンボル等）を描画する。
    /// </summary>
    /// <param name="canvas">描画先 SkiaSharp キャンバス（画面座標系）。</param>
    /// <param name="tile">対象ベクタータイルデータ。</param>
    /// <param name="tileToCanvasMatrix">タイルローカル座標から画面ピクセル座標への変換行列。</param>
    /// <param name="zoom">現在の表示ズームレベル。</param>
    /// <param name="collisionGrid">ラベル衝突判定グリッド。</param>
    /// <param name="viewport">描画画面全体の矩形。</param>
    void RenderLabels(
        SKCanvas canvas,
        VectorTile tile,
        SKMatrix tileToCanvasMatrix,
        int zoom,
        LabelCollisionGrid collisionGrid,
        SKRect viewport);
}
