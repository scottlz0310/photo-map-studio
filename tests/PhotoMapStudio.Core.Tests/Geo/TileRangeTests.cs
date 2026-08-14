using PhotoMapStudio.Core.Geo;

namespace PhotoMapStudio.Core.Tests.Geo;

public class TileRangeTests
{
    [Theory]
    // 中心 (0.5, 0.5) はワールドピクセル (128, 128)。緯度経度 0 度をまたぐと左上が負値になる
    [InlineData(800, 600, -2, 2, -1, 1, 240.0, 84.0)]
    [InlineData(100, 100, 0, 0, 0, 0, 78.0, 78.0)]
    [InlineData(256, 256, 0, 1, 0, 1, 0.0, 0.0)]
    [InlineData(512, 512, -1, 1, -1, 1, 128.0, 128.0)]
    public void 中心と出力サイズからタイル範囲を決定する(
        int width,
        int height,
        int expectedMinX,
        int expectedMaxX,
        int expectedMinY,
        int expectedMaxY,
        double expectedCropLeft,
        double expectedCropTop)
    {
        TilePoint center = WebMercator.ToTilePoint(new GeoCoordinate(0.0, 0.0), 0);

        TileRange range = TileRange.Compute(center, width, height);

        Assert.Equal(expectedMinX, range.MinX);
        Assert.Equal(expectedMaxX, range.MaxX);
        Assert.Equal(expectedMinY, range.MinY);
        Assert.Equal(expectedMaxY, range.MaxY);
        Assert.Equal(expectedCropLeft, range.CropLeft, 9);
        Assert.Equal(expectedCropTop, range.CropTop, 9);
    }

    [Fact]
    public void キャンバスは切り出し領域を包含する()
    {
        TilePoint center = WebMercator.ToTilePoint(new GeoCoordinate(35.681166, 139.767111), 15);

        TileRange range = TileRange.Compute(center, 800, 600);

        Assert.Equal(range.TileCountX * WebMercator.TileSize, range.CanvasWidth);
        Assert.Equal(range.TileCountY * WebMercator.TileSize, range.CanvasHeight);
        Assert.InRange(range.CropLeft, 0.0, WebMercator.TileSize);
        Assert.InRange(range.CropTop, 0.0, WebMercator.TileSize);
        Assert.True(range.CropLeft + 800 <= range.CanvasWidth);
        Assert.True(range.CropTop + 600 <= range.CanvasHeight);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, 0)]
    [InlineData(-1, 600)]
    [InlineData(800, -1)]
    public void 出力サイズが正でなければ例外を投げる(int width, int height)
    {
        TilePoint center = WebMercator.ToTilePoint(new GeoCoordinate(0.0, 0.0), 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => TileRange.Compute(center, width, height));
    }
}
