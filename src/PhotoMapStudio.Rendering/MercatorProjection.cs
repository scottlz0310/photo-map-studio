namespace PhotoMapStudio.Rendering;

/// <summary>
/// Web メルカトル図法（Slippy Map）の座標変換。Core.Geo.WebMercator と同じ式で、
/// 描画層が Core に依存しないために内部へ持つ。乖離は MercatorProjectionTests が検出する。
/// </summary>
internal static class MercatorProjection
{
    /// <summary>タイル 1 辺のピクセル数。</summary>
    public const int TileSize = 256;

    /// <summary>
    /// 緯度経度を、指定ズーム（連続値）のワールドピクセル座標へ変換する。
    /// </summary>
    public static (double X, double Y) ToWorldPixel(double latitude, double longitude, double zoom)
    {
        double n = Math.Pow(2.0, zoom);
        double x = ((longitude + 180.0) / 360.0) * n;

        double latitudeRadian = latitude * Math.PI / 180.0;
        // ln(tan(φ) + sec(φ)) と等価。asinh の方が極付近で数値的に安定する
        double y = (1.0 - (Math.Asinh(Math.Tan(latitudeRadian)) / Math.PI)) / 2.0 * n;

        return (x * TileSize, y * TileSize);
    }
}
