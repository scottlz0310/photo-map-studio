using System.Text.RegularExpressions;

using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Tiles;

public class TileCacheKeyTests
{
    private const string OsmTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    private const string GsiTemplate = "https://cyberjapandata.gsi.go.jp/xyz/pale/{z}/{x}/{y}.png";

    [Fact]
    public void URLテンプレートが異なればキーも異なる()
    {
        string osm = TileCacheKey.Create(OsmTemplate, 15, 29105, 12903);
        string gsi = TileCacheKey.Create(GsiTemplate, 15, 29105, 12903);

        Assert.NotEqual(osm, gsi);
    }

    [Fact]
    public void 同一テンプレートと同一座標ではキーが一致する()
        => Assert.Equal(
            TileCacheKey.Create(OsmTemplate, 15, 29105, 12903),
            TileCacheKey.Create(OsmTemplate, 15, 29105, 12903));

    [Theory]
    [InlineData(15, 29105, 12903)]
    [InlineData(0, 0, 0)]
    [InlineData(19, 524287, 524287)]
    public void キーはハッシュとタイル座標で構成される(int zoom, int x, int y)
    {
        string key = TileCacheKey.Create(OsmTemplate, zoom, x, y);

        Assert.Matches(new Regex($"^[0-9a-f]{{8}}_{zoom}_{x}_{y}\\.png$", RegexOptions.None, TimeSpan.FromSeconds(1)), key);
    }

    [Theory]
    [InlineData(15, 29105, 12903, 15, 29105, 12904)]
    [InlineData(15, 29105, 12903, 16, 29105, 12903)]
    [InlineData(15, 29105, 12903, 15, 29106, 12903)]
    public void タイル座標が異なればキーも異なる(int z1, int x1, int y1, int z2, int x2, int y2)
        => Assert.NotEqual(
            TileCacheKey.Create(OsmTemplate, z1, x1, y1),
            TileCacheKey.Create(OsmTemplate, z2, x2, y2));

    [Fact]
    public void キーはパス区切りを含まない()
    {
        string key = TileCacheKey.Create(OsmTemplate, 15, 29105, 12903);

        Assert.Equal(key, Path.GetFileName(key));
    }
}
