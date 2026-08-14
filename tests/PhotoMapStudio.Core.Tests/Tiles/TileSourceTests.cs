using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Tiles;

public class TileSourceTests
{
    [Theory]
    [InlineData("https://tile.openstreetmap.org/{z}/{x}/{y}.png", 15, 29105, 12903, "https://tile.openstreetmap.org/15/29105/12903.png")]
    [InlineData("https://cyberjapandata.gsi.go.jp/xyz/pale/{z}/{x}/{y}.png", 5, 0, 0, "https://cyberjapandata.gsi.go.jp/xyz/pale/5/0/0.png")]
    public void プレースホルダーを置換してURLを組み立てる(string template, int zoom, int x, int y, string expected)
    {
        var source = new TileSource("テスト", template, 0, 19, "出典", TileRateLimit.Conservative);

        Assert.Equal(new Uri(expected), source.BuildTileUri(zoom, x, y));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(18, true)]
    [InlineData(19, false)]
    public void ズーム範囲を判定する(int zoom, bool expected)
        => Assert.Equal(expected, TileSources.GsiPale.SupportsZoom(zoom));

    [Theory]
    [InlineData("https://tile.example.com/{x}/{y}.png")]
    [InlineData("https://tile.example.com/{z}/{y}.png")]
    [InlineData("https://tile.example.com/{z}/{x}.png")]
    [InlineData("tile.example.com/{z}/{x}/{y}.png")]
    [InlineData("file:///C:/tiles/{z}/{x}/{y}.png")]
    [InlineData("  ")]
    public void 不正なURLテンプレートは受け付けない(string template)
        => Assert.Throws<ArgumentException>(
            () => new TileSource("テスト", template, 0, 19, "出典", TileRateLimit.Conservative));

    [Theory]
    [InlineData(-1, 19)]
    [InlineData(10, 9)]
    public void 不正なズーム範囲は受け付けない(int minZoom, int maxZoom)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TileSource(
            "テスト",
            "https://tile.example.com/{z}/{x}/{y}.png",
            minZoom,
            maxZoom,
            "出典",
            TileRateLimit.Conservative));

    [Fact]
    public void プリセットは出典とズーム範囲を持つ()
    {
        Assert.All(TileSources.All, source =>
        {
            Assert.False(string.IsNullOrWhiteSpace(source.Attribution));
            Assert.True(source.MinZoom <= source.MaxZoom);
            Assert.True(source.RateLimit.MaxConcurrentRequests > 0);
        });

        Assert.Equal("© OpenStreetMap contributors", TileSources.OpenStreetMap.Attribution);
        Assert.Equal(
            new Uri("https://www.openstreetmap.org/copyright"),
            TileSources.OpenStreetMap.AttributionUri);
        Assert.Equal(0, TileSources.OpenStreetMap.MinZoom);
        Assert.Equal(19, TileSources.OpenStreetMap.MaxZoom);
        Assert.Equal(1, TileSources.OpenStreetMap.RateLimit.MaxConcurrentRequests);
    }

    [Fact]
    public void 任意URLからカスタムソースを構築できる()
    {
        TileSource source = TileSources.Custom("https://tile.example.com/{z}/{x}/{y}.png", "自前の出典");

        Assert.Equal("自前の出典", source.Attribution);
        Assert.Equal(new Uri("https://tile.example.com/3/1/2.png"), source.BuildTileUri(3, 1, 2));
    }
}
