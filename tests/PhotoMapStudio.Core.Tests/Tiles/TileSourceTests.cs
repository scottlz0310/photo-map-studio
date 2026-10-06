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

    [Theory]
    [InlineData("http://localhost:8765/{z}/{x}/{y}.png")]
    [InlineData("http://LOCALHOST/{z}/{x}/{y}.png")]
    [InlineData("http://localhost./{z}/{x}/{y}.png")]
    [InlineData("http://127.0.0.1:8765/v1/{z}/{x}/{y}.png")]
    [InlineData("http://127.1.2.3/{z}/{x}/{y}.png")]
    [InlineData("http://[::1]:8765/{z}/{x}/{y}.png")]
    [InlineData("https://127.0.0.1/{z}/{x}/{y}.png")]
    public void ループバックの配信元はプレビューも一括生成も同じ高速なレートを使う(string template)
    {
        TileSource source = TileSources.Custom(template, "出典");

        Assert.True(source.IsLoopback);
        Assert.True(TileSource.IsLoopbackUrlTemplate(template));
        Assert.Equal(TileRateLimit.Loopback, source.RateLimit);
        Assert.Equal(source.RateLimit, source.BatchRateLimit);
    }

    [Fact]
    public void ループバック用のレートは8並列5ミリ秒間隔にする()
    {
        Assert.Equal(8, TileRateLimit.Loopback.MaxConcurrentRequests);
        Assert.Equal(TimeSpan.FromMilliseconds(5), TileRateLimit.Loopback.MinimumInterval);
    }

    [Fact]
    public void ループバックの一括生成レートは配信元自身のレートに従う()
    {
        var source = new TileSource("テスト", "http://127.0.0.1/{z}/{x}/{y}.png", 0, 19, "出典", TileRateLimit.OpenStreetMap);

        Assert.Equal(TileRateLimit.OpenStreetMap, source.BatchRateLimit);
    }

    [Theory]
    [InlineData("https://tile.example.com/{z}/{x}/{y}.png")]
    [InlineData("http://192.168.1.10:8080/{z}/{x}/{y}.png")]
    [InlineData("http://10.0.0.5/{z}/{x}/{y}.png")]
    [InlineData("http://172.16.0.1/{z}/{x}/{y}.png")]
    [InlineData("http://169.254.1.1/{z}/{x}/{y}.png")]
    [InlineData("http://0.0.0.0/{z}/{x}/{y}.png")]
    [InlineData("http://[fe80::1]/{z}/{x}/{y}.png")]
    [InlineData("http://localhost.example.com/{z}/{x}/{y}.png")]
    [InlineData("http://127.0.0.1.example.com/{z}/{x}/{y}.png")]
    [InlineData("http://notlocalhost/{z}/{x}/{y}.png")]
    public void ループバック以外の配信元は従来どおり1接続1秒間隔にする(string template)
    {
        TileSource source = TileSources.Custom(template, "出典");

        Assert.False(source.IsLoopback);
        Assert.False(TileSource.IsLoopbackUrlTemplate(template));
        Assert.Equal(TileRateLimit.Conservative, source.RateLimit);
        Assert.Equal(new TileRateLimit(1, TimeSpan.FromSeconds(1)), source.BatchRateLimit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("localhost/{z}/{x}/{y}.png")]
    [InlineData("ftp://localhost/{z}/{x}/{y}.png")]
    [InlineData("file:///C:/tiles/{z}/{x}/{y}.png")]
    public void 不正または非HTTPのテンプレートはループバックと判定しない(string? template)
        => Assert.False(TileSource.IsLoopbackUrlTemplate(template));

    [Fact]
    public void 地理院の一括生成レートは通常のレートのままにする()
        => Assert.Equal(TileSources.GsiPale.RateLimit, TileSources.GsiPale.BatchRateLimit);
}
