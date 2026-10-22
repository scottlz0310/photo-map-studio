using PhotoMapStudio.App.Models;
using PhotoMapStudio.Core.Maps;

namespace PhotoMapStudio.App.Tests.Models;

public sealed class PinAnchorChoiceTests
{
    [Fact]
    public void 全てのアンカー基準点選択肢がカタログに含まれる()
    {
        Assert.Equal(2, PinAnchorChoice.All.Count);
        Assert.Contains(PinAnchorChoice.All, c => c.Anchor == PinAnchor.BottomCenter);
        Assert.Contains(PinAnchorChoice.All, c => c.Anchor == PinAnchor.Center);
    }

    [Fact]
    public void 既定のアンカーはBottomCenterである()
    {
        Assert.Equal(PinAnchor.BottomCenter, PinAnchorChoice.Default.Anchor);
    }

    [Theory]
    [InlineData(PinAnchor.BottomCenter, "下端中央 (ピン先端が地点)")]
    [InlineData(PinAnchor.Center, "中央 (図心・ターゲット中心が地点)")]
    public void FromAnchorで対応する選択肢を取得できる(PinAnchor anchor, string expectedDisplayName)
    {
        PinAnchorChoice choice = PinAnchorChoice.FromAnchor(anchor);
        Assert.Equal(anchor, choice.Anchor);
        Assert.Equal(expectedDisplayName, choice.DisplayName);
    }
}
