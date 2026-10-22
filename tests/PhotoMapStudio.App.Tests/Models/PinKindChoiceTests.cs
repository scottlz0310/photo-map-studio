using PhotoMapStudio.App.Models;
using PhotoMapStudio.Core.Maps;

namespace PhotoMapStudio.App.Tests.Models;

public sealed class PinKindChoiceTests
{
    [Fact]
    public void 全てのピン形状選択肢がカタログに含まれる()
    {
        Assert.Equal(6, PinKindChoice.All.Count);
        Assert.Contains(PinKindChoice.All, c => c.Kind == PinKind.GreenPin);
        Assert.Contains(PinKindChoice.All, c => c.Kind == PinKind.RedPin);
        Assert.Contains(PinKindChoice.All, c => c.Kind == PinKind.BluePin);
        Assert.Contains(PinKindChoice.All, c => c.Kind == PinKind.TargetCircle);
        Assert.Contains(PinKindChoice.All, c => c.Kind == PinKind.Crosshair);
        Assert.Contains(PinKindChoice.All, c => c.Kind == PinKind.CustomImage);
    }

    [Fact]
    public void 既定のピン形状はGreenPinである()
    {
        Assert.Equal(PinKind.GreenPin, PinKindChoice.Default.Kind);
    }

    [Theory]
    [InlineData(PinKind.GreenPin, "緑ピン (同梱)")]
    [InlineData(PinKind.RedPin, "赤ピン (同梱)")]
    [InlineData(PinKind.BluePin, "青ピン (同梱)")]
    [InlineData(PinKind.TargetCircle, "二重円ターゲット")]
    [InlineData(PinKind.Crosshair, "十字ターゲット (測量風)")]
    [InlineData(PinKind.CustomImage, "カスタム画像...")]
    public void FromKindで対応する選択肢を取得できる(PinKind kind, string expectedDisplayName)
    {
        PinKindChoice choice = PinKindChoice.FromKind(kind);
        Assert.Equal(kind, choice.Kind);
        Assert.Equal(expectedDisplayName, choice.DisplayName);
    }
}
