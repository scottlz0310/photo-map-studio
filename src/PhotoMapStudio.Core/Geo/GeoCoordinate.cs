namespace PhotoMapStudio.Core.Geo;

/// <summary>
/// 十進度で表した地理座標。
/// </summary>
/// <param name="Latitude">緯度（北緯が正）。</param>
/// <param name="Longitude">経度（東経が正）。</param>
public readonly record struct GeoCoordinate(double Latitude, double Longitude);
