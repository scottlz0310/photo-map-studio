namespace PhotoMapStudio.Core.Geo;

/// <summary>
/// 出力画像を賄うために必要なタイルの範囲と、タイルを貼り合わせたキャンバスからの切り出し位置。
/// </summary>
/// <param name="MinX">左端のタイル番号。</param>
/// <param name="MinY">上端のタイル番号。</param>
/// <param name="MaxX">右端のタイル番号。</param>
/// <param name="MaxY">下端のタイル番号。</param>
/// <param name="CropLeft">キャンバス左端から切り出し位置までのピクセル数（切り捨て）。</param>
/// <param name="CropTop">キャンバス上端から切り出し位置までのピクセル数（切り捨て）。</param>
public readonly record struct TileRange(
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    int CropLeft,
    int CropTop)
{
    /// <summary>X 方向のタイル枚数。</summary>
    public int TileCountX => MaxX - MinX + 1;

    /// <summary>Y 方向のタイル枚数。</summary>
    public int TileCountY => MaxY - MinY + 1;

    /// <summary>タイルを貼り合わせたキャンバスの幅。</summary>
    public int CanvasWidth => TileCountX * WebMercator.TileSize;

    /// <summary>タイルを貼り合わせたキャンバスの高さ。</summary>
    public int CanvasHeight => TileCountY * WebMercator.TileSize;

    /// <summary>
    /// 中心座標と出力サイズから必要なタイル範囲を決定する。
    /// </summary>
    /// <param name="center">出力画像の中心にあたるタイル座標。</param>
    /// <param name="width">出力画像の幅（ピクセル）。</param>
    /// <param name="height">出力画像の高さ（ピクセル）。</param>
    /// <returns>必要なタイル範囲。</returns>
    public static TileRange Compute(TilePoint center, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        double leftPixel = center.WorldPixelX - (width / 2.0);
        double topPixel = center.WorldPixelY - (height / 2.0);
        double rightPixel = center.WorldPixelX + (width / 2.0);
        double bottomPixel = center.WorldPixelY + (height / 2.0);

        // 経度 0 度・緯度 0 度をまたぐと負値になるため、0 方向へ丸めるキャストではなく床関数を使う
        int minX = (int)Math.Floor(leftPixel / WebMercator.TileSize);
        int minY = (int)Math.Floor(topPixel / WebMercator.TileSize);
        int maxX = (int)Math.Floor(rightPixel / WebMercator.TileSize);
        int maxY = (int)Math.Floor(bottomPixel / WebMercator.TileSize);

        // 切り出し位置は切り捨てとし、サブピクセル補間は行わない（タイル境界のにじみを避けるため）
        return new TileRange(
            minX,
            minY,
            maxX,
            maxY,
            (int)Math.Floor(leftPixel - ((double)minX * WebMercator.TileSize)),
            (int)Math.Floor(topPixel - ((double)minY * WebMercator.TileSize)));
    }
}
