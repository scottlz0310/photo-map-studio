using PhotoMapStudio.Core.Geo;

namespace PhotoMapStudio.Core.Tests.Geo;

public class WebMercatorTests
{
    [Theory]
    [InlineData(0.0, 0.0, 0, 0.5, 0.5)]
    [InlineData(0.0, -180.0, 0, 0.0, 0.5)]
    [InlineData(0.0, 180.0, 0, 1.0, 0.5)]
    [InlineData(0.0, 0.0, 1, 1.0, 1.0)]
    [InlineData(35.681166, 139.767111, 15, 29105.913036, 12903.325595)]
    public void 緯度経度をタイル座標へ変換する(double latitude, double longitude, int zoom, double expectedX, double expectedY)
    {
        TilePoint actual = WebMercator.ToTilePoint(new GeoCoordinate(latitude, longitude), zoom);

        Assert.Equal(expectedX, actual.X, 5);
        Assert.Equal(expectedY, actual.Y, 5);
    }

    [Theory]
    [InlineData(85.0511, 0.0)]
    [InlineData(-85.0511, 1.0)]
    public void メルカトルの南北限はタイル座標の端に対応する(double latitude, double expectedY)
    {
        TilePoint actual = WebMercator.ToTilePoint(new GeoCoordinate(latitude, 0.0), 0);

        Assert.Equal(expectedY, actual.Y, 5);
    }

    [Fact]
    public void ズーム十九でも有限値に収まる()
    {
        TilePoint actual = WebMercator.ToTilePoint(new GeoCoordinate(35.681166, 139.767111), 19);

        double tileCount = Math.Pow(2.0, 19);
        Assert.InRange(actual.X, 0.0, tileCount);
        Assert.InRange(actual.Y, 0.0, tileCount);
    }

    [Fact]
    public void ワールドピクセル座標はタイル座標の二百五十六倍である()
    {
        TilePoint point = WebMercator.ToTilePoint(new GeoCoordinate(0.0, 0.0), 2);

        Assert.Equal(point.X * WebMercator.TileSize, point.WorldPixelX);
        Assert.Equal(point.Y * WebMercator.TileSize, point.WorldPixelY);
    }
}
