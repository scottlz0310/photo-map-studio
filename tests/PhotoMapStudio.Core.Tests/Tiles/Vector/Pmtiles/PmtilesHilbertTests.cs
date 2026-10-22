using PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector.Pmtiles;

public sealed class PmtilesHilbertTests
{
    [Theory]
    [InlineData(0, 0, 0, 0UL)]
    [InlineData(1, 0, 0, 1UL)]
    [InlineData(1, 0, 1, 2UL)]
    [InlineData(1, 1, 1, 3UL)]
    [InlineData(1, 1, 0, 4UL)]
    [InlineData(2, 0, 0, 5UL)]
    [InlineData(2, 3, 0, 20UL)]
    [InlineData(3, 0, 0, 21UL)]
    [InlineData(4, 0, 0, 85UL)]
    [InlineData(5, 0, 0, 341UL)]
    [InlineData(6, 0, 0, 1365UL)]
    public void 既知の座標を正しく変換する(int z, int x, int y, ulong expectedTileId)
    {
        ulong actual = PmtilesHilbert.ZxyToTileId(z, x, y);
        Assert.Equal(expectedTileId, actual);

        var (actualZ, actualX, actualY) = PmtilesHilbert.TileIdToZxy(actual);
        Assert.Equal(z, actualZ);
        Assert.Equal(x, actualX);
        Assert.Equal(y, actualY);
    }

    [Theory]
    [InlineData(8, 227, 102)] // 東京近郊 (z=8)
    [InlineData(14, 14552, 6451)] // 東京駅 (z=14)
    [InlineData(16, 58208, 25804)] // 東京駅 (z=16)
    public void 高ズーム実座標で相互変換が一貫する(int z, int x, int y)
    {
        ulong tileId = PmtilesHilbert.ZxyToTileId(z, x, y);
        Assert.True(tileId > 0);

        var (recoveredZ, recoveredX, recoveredY) = PmtilesHilbert.TileIdToZxy(tileId);
        Assert.Equal(z, recoveredZ);
        Assert.Equal(x, recoveredX);
        Assert.Equal(y, recoveredY);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(27, 0, 0)]
    [InlineData(2, -1, 0)]
    [InlineData(2, 4, 0)]
    [InlineData(2, 0, -1)]
    [InlineData(2, 0, 4)]
    public void 範囲外の引数でArgumentOutOfRangeExceptionを送出する(int z, int x, int y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PmtilesHilbert.ZxyToTileId(z, x, y));
    }
}
