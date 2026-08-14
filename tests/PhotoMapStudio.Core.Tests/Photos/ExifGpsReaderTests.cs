using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Photos;

using Directory = System.IO.Directory;

namespace PhotoMapStudio.Core.Tests.Photos;

public sealed class ExifGpsReaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pms-exif-{Guid.NewGuid():N}");

    public ExifGpsReaderTests() => Directory.CreateDirectory(this.root);

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    [Fact]
    public void 四タグが揃っていれば座標を返す()
    {
        GpsDirectory directory = CreateGpsDirectory(
            latitudeRef: "N",
            longitudeRef: "E",
            includeLatitude: true,
            includeLongitude: true);

        GeoCoordinate? actual = ExifGpsReader.TryReadCoordinate(directory);

        Assert.NotNull(actual);
        Assert.Equal(35.6811666666667, actual.Value.Latitude, 9);
        Assert.Equal(139.7671111111111, actual.Value.Longitude, 9);
    }

    [Theory]
    [InlineData(null, "E", true, true)]
    [InlineData("N", null, true, true)]
    [InlineData("N", "E", false, true)]
    [InlineData("N", "E", true, false)]
    public void 四タグが揃っていなければGPS情報なしとして扱う(
        string? latitudeRef,
        string? longitudeRef,
        bool includeLatitude,
        bool includeLongitude)
    {
        GpsDirectory directory = CreateGpsDirectory(latitudeRef, longitudeRef, includeLatitude, includeLongitude);

        Assert.Null(ExifGpsReader.TryReadCoordinate(directory));
    }

    [Fact]
    public void 南緯西経は符号が反転する()
    {
        GpsDirectory directory = CreateGpsDirectory("S", "W", includeLatitude: true, includeLongitude: true);

        GeoCoordinate? actual = ExifGpsReader.TryReadCoordinate(directory);

        Assert.NotNull(actual);
        Assert.Equal(-35.6811666666667, actual.Value.Latitude, 9);
        Assert.Equal(-139.7671111111111, actual.Value.Longitude, 9);
    }

    [Fact]
    public void 存在しないファイルは読み取り失敗として通知する()
    {
        string path = Path.Combine(this.root, "missing.jpg");

        ExifGpsReadException exception = Assert.Throws<ExifGpsReadException>(() => new ExifGpsReader().Read(path));

        Assert.Equal(path, exception.FilePath);
        Assert.IsAssignableFrom<IOException>(exception.InnerException);
    }

    [Fact]
    public void 画像として解析できないファイルは読み取り失敗として通知する()
    {
        string path = Path.Combine(this.root, "broken.jpg");
        File.WriteAllText(path, "not an image");

        ExifGpsReadException exception = Assert.Throws<ExifGpsReadException>(() => new ExifGpsReader().Read(path));

        Assert.Equal(path, exception.FilePath);
        Assert.IsType<ImageProcessingException>(exception.InnerException);
    }

    private static GpsDirectory CreateGpsDirectory(
        string? latitudeRef,
        string? longitudeRef,
        bool includeLatitude,
        bool includeLongitude)
    {
        var directory = new GpsDirectory();

        if (latitudeRef is not null)
        {
            directory.Set(GpsDirectory.TagLatitudeRef, latitudeRef);
        }

        if (longitudeRef is not null)
        {
            directory.Set(GpsDirectory.TagLongitudeRef, longitudeRef);
        }

        if (includeLatitude)
        {
            directory.Set(GpsDirectory.TagLatitude, new Rational[] { new(35, 1), new(40, 1), new(522, 10) });
        }

        if (includeLongitude)
        {
            directory.Set(GpsDirectory.TagLongitude, new Rational[] { new(139, 1), new(46, 1), new(16, 10) });
        }

        return directory;
    }
}
