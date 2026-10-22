using PhotoMapStudio.App.Services;
using PhotoMapStudio.Core.Maps;

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

    [Theory]
    [InlineData(PinKind.GreenPin, "green_pin.png")]
    [InlineData(PinKind.RedPin, "red_pin.png")]
    [InlineData(PinKind.BluePin, "blue_pin.png")]
    public void 同梱ピン種別に応じたアセットパスへ解決する(PinKind kind, string fileName)
    {
        string expected = Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins", fileName);
        Assert.Equal(expected, PhotoMapAssetPaths.ResolvePinImagePath(kind, customPath: null));
    }

    [Theory]
    [InlineData(PinKind.TargetCircle)]
    [InlineData(PinKind.Crosshair)]
    public void プロシージャル描画ピン種別の画像パスはnullとなる(PinKind kind)
    {
        Assert.Null(PhotoMapAssetPaths.ResolvePinImagePath(kind, customPath: "dummy.png"));
    }

    [Fact]
    public void カスタム画像指定時は指定パスを解決する()
    {
        Assert.Equal(
            "C:\\custom\\pin.png",
            PhotoMapAssetPaths.ResolvePinImagePath(PinKind.CustomImage, "  C:\\custom\\pin.png  "));
    }

    [Fact]
    public void カスタム画像でパス未指定時は同梱グリーンピンを返す()
    {
        string expected = Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins", "green_pin.png");
        Assert.Equal(expected, PhotoMapAssetPaths.ResolvePinImagePath(PinKind.CustomImage, "   "));
    }
}
