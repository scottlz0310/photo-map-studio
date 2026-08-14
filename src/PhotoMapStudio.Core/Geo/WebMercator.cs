namespace PhotoMapStudio.Core.Geo;

/// <summary>
/// Web メルカトル図法（Slippy Map）の座標変換。
/// </summary>
public static class WebMercator
{
    /// <summary>タイル 1 辺のピクセル数。</summary>
    public const int TileSize = 256;

    /// <summary>
    /// 地理座標をタイル座標へ変換する。
    /// </summary>
    /// <param name="coordinate">変換元の地理座標。</param>
    /// <param name="zoom">ズームレベル。</param>
    /// <returns>浮動小数点のタイル座標。</returns>
    public static TilePoint ToTilePoint(GeoCoordinate coordinate, int zoom)
    {
        double n = Math.Pow(2.0, zoom);
        double x = ((coordinate.Longitude + 180.0) / 360.0) * n;

        double latitudeRadian = coordinate.Latitude * Math.PI / 180.0;
        // ln(tan(φ) + sec(φ)) と等価。asinh の方が極付近で数値的に安定する
        double y = (1.0 - (Math.Asinh(Math.Tan(latitudeRadian)) / Math.PI)) / 2.0 * n;

        return new TilePoint(x, y);
    }
}
