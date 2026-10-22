using System.Text.RegularExpressions;

using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class VectorTileCacheKeyTests
{
    private const string OfmSource = "openfreemap";
    private const string GsiSource = "gsi-vector";

    [Fact]
    public void ソース識別子が異なればキーも異なる()
    {
        string ofm = VectorTileCacheKey.Create(OfmSource, 14, 14675, 5991);
        string gsi = VectorTileCacheKey.Create(GsiSource, 14, 14675, 5991);

        Assert.NotEqual(ofm, gsi);
    }

    [Fact]
    public void 同一ソースと同一座標ではキーが一致する()
        => Assert.Equal(
            VectorTileCacheKey.Create(OfmSource, 14, 14675, 5991),
            VectorTileCacheKey.Create(OfmSource, 14, 14675, 5991));

    [Theory]
    [InlineData(14, 14675, 5991)]
    [InlineData(0, 0, 0)]
    [InlineData(16, 65535, 65535)]
    public void キーはハッシュとタイル座標で構成され拡張子pbfを持つ(int zoom, int x, int y)
    {
        string key = VectorTileCacheKey.Create(OfmSource, zoom, x, y);

        Assert.Matches(new Regex($"^[0-9a-f]{{8}}_{zoom}_{x}_{y}\\.pbf$", RegexOptions.None, TimeSpan.FromSeconds(1)), key);
    }

    [Theory]
    [InlineData(14, 14675, 5991, 14, 14675, 5992)]
    [InlineData(14, 14675, 5991, 15, 14675, 5991)]
    [InlineData(14, 14675, 5991, 14, 14676, 5991)]
    public void タイル座標が異なればキーも異なる(int z1, int x1, int y1, int z2, int x2, int y2)
        => Assert.NotEqual(
            VectorTileCacheKey.Create(OfmSource, z1, x1, y1),
            VectorTileCacheKey.Create(OfmSource, z2, x2, y2));

    [Fact]
    public void キーはパス区切りを含まない()
    {
        string key = VectorTileCacheKey.Create(OfmSource, 14, 14675, 5991);

        Assert.DoesNotContain("/", key, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", key, StringComparison.Ordinal);
    }
}
