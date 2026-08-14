namespace PhotoMapStudio.Core.Geo;

/// <summary>
/// ズームレベル上のタイル座標。整数部がタイル番号、小数部がタイル内の相対位置を表す。
/// </summary>
/// <param name="X">X 方向のタイル座標。</param>
/// <param name="Y">Y 方向のタイル座標。</param>
public readonly record struct TilePoint(double X, double Y)
{
    /// <summary>ワールドピクセル座標の X 成分。</summary>
    public double WorldPixelX => X * WebMercator.TileSize;

    /// <summary>ワールドピクセル座標の Y 成分。</summary>
    public double WorldPixelY => Y * WebMercator.TileSize;
}
