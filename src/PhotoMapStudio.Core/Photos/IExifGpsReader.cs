using PhotoMapStudio.Core.Geo;

namespace PhotoMapStudio.Core.Photos;

/// <summary>
/// 写真の EXIF から GPS 座標を読み取る。
/// </summary>
public interface IExifGpsReader
{
    /// <summary>
    /// 指定した写真の GPS 座標を読み取る。
    /// </summary>
    /// <param name="filePath">写真ファイルのパス。</param>
    /// <returns>GPS 座標。GPS 情報を持たない場合は <see langword="null"/>。</returns>
    /// <exception cref="ExifGpsReadException">ファイルまたはメタデータの読み取りに失敗した場合。</exception>
    GeoCoordinate? Read(string filePath);
}
