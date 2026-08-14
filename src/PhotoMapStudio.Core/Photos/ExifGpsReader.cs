using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using PhotoMapStudio.Core.Geo;

namespace PhotoMapStudio.Core.Photos;

/// <summary>
/// MetadataExtractor による <see cref="IExifGpsReader"/> の実装。
/// </summary>
public sealed class ExifGpsReader : IExifGpsReader
{
    /// <inheritdoc />
    public GeoCoordinate? Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (ImageProcessingException ex)
        {
            throw new ExifGpsReadException($"メタデータを解析できませんでした: {filePath}", filePath, ex);
        }
        catch (IOException ex)
        {
            throw new ExifGpsReadException($"ファイルを読み取れませんでした: {filePath}", filePath, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ExifGpsReadException($"ファイルへのアクセスが拒否されました: {filePath}", filePath, ex);
        }

        GpsDirectory? gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        return gps is null ? null : TryReadCoordinate(gps);
    }

    /// <summary>
    /// GPS IFD から座標を取り出す。
    /// </summary>
    /// <param name="directory">GPS ディレクトリ。</param>
    /// <returns>座標。必要な 4 タグが揃っていない場合は <see langword="null"/>。</returns>
    public static GeoCoordinate? TryReadCoordinate(GpsDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        Rational[]? latitude = directory.GetRationalArray(GpsDirectory.TagLatitude);
        string? latitudeRef = directory.GetString(GpsDirectory.TagLatitudeRef);
        Rational[]? longitude = directory.GetRationalArray(GpsDirectory.TagLongitude);
        string? longitudeRef = directory.GetString(GpsDirectory.TagLongitudeRef);

        // 部分的な値からの推定はしない
        if (latitude is null || latitudeRef is null || longitude is null || longitudeRef is null)
        {
            return null;
        }

        return new GeoCoordinate(
            DmsCoordinate.ToDecimalDegrees(latitude, latitudeRef),
            DmsCoordinate.ToDecimalDegrees(longitude, longitudeRef));
    }
}
