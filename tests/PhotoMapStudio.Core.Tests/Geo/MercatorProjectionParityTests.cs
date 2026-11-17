using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Rendering;

namespace PhotoMapStudio.Core.Tests.Geo;

/// <summary>
/// 描画層（Rendering）が内部に持つメルカトル式が、Core.Geo.WebMercator とずれないことを保証する。
/// 描画層は Core に依存できないため、式が 2 か所にある。
/// </summary>
public class MercatorProjectionParityTests
{
    [Theory]
    [InlineData(0.0, 0.0, 0)]
    [InlineData(35.681166, 139.767111, 15)]
    [InlineData(48.8584, 2.2945, 14)]
    [InlineData(-33.8688, 151.2093, 10)]
    [InlineData(85.0511, -180.0, 3)]
    [InlineData(-85.0511, 180.0, 19)]
    [InlineData(0.0, 179.99, 12)]
    public void ワールドピクセル座標がWebMercatorと一致する(double latitude, double longitude, int zoom)
    {
        TilePoint expected = WebMercator.ToTilePoint(new GeoCoordinate(latitude, longitude), zoom);

        (double x, double y) = MercatorProjection.ToWorldPixel(latitude, longitude, zoom);

        Assert.Equal(expected.WorldPixelX, x, 9);
        Assert.Equal(expected.WorldPixelY, y, 9);
    }

    [Fact]
    public void タイル1辺のピクセル数がWebMercatorと一致する()
    {
        Assert.Equal(WebMercator.TileSize, MercatorProjection.TileSize);
    }
}
