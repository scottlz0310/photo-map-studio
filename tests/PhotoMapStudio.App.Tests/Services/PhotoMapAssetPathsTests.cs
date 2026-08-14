using PhotoMapStudio.App.Services;

namespace PhotoMapStudio.App.Tests.Services;

public class PhotoMapAssetPathsTests
{
    [Fact]
    public void 空の設定は同梱グリーンピンへ解決する()
    {
        string expected = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "MapPins",
            "green_pin.png");

        Assert.Equal(expected, PhotoMapAssetPaths.ResolvePinImagePath("  "));
    }

    [Fact]
    public void 指定されたピン画像を優先する()
        => Assert.Equal(
            "C:\\custom\\pin.png",
            PhotoMapAssetPaths.ResolvePinImagePath(" C:\\custom\\pin.png "));
}
