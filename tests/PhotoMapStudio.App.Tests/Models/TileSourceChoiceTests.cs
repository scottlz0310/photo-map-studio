using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.Tests.Models;

public sealed class TileSourceChoiceTests
{
    [Fact]
    public void 全ての選択肢がカタログに含まれる()
    {
        Assert.Contains(TileSourceChoices.GsiPale, TileSourceChoices.All);
        Assert.Contains(TileSourceChoices.GsiStandard, TileSourceChoices.All);
        Assert.Contains(TileSourceChoices.GsiVector, TileSourceChoices.All);
        Assert.Contains(TileSourceChoices.OpenFreeMap, TileSourceChoices.All);
        Assert.Contains(TileSourceChoices.OpenStreetMap, TileSourceChoices.All);
        Assert.Contains(TileSourceChoices.Custom, TileSourceChoices.All);
    }

    [Fact]
    public void OpenFreeMap選択肢はベクター設定を持つ()
    {
        TileSourceChoice choice = TileSourceChoices.OpenFreeMap;
        Assert.Equal(TileSourceChoices.OpenFreeMapKey, choice.Key);
        Assert.False(choice.IsCustom);
        Assert.NotNull(choice.Source);
        Assert.True(choice.Source.IsVector);
        Assert.Equal(0, choice.MinZoom);
        Assert.Equal(19, choice.MaxZoom);
    }

    [Fact]
    public void GsiVector選択肢はベクター設定を持つ()
    {
        TileSourceChoice choice = TileSourceChoices.GsiVector;
        Assert.Equal(TileSourceChoices.GsiVectorKey, choice.Key);
        Assert.False(choice.IsCustom);
        Assert.NotNull(choice.Source);
        Assert.True(choice.Source.IsVector);
        Assert.Equal(4, choice.MinZoom);
        Assert.Equal(18, choice.MaxZoom);
    }

    [Theory]
    [InlineData("gsi-pale", "gsi-pale")]
    [InlineData("gsi-standard", "gsi-standard")]
    [InlineData("gsi-vector", "gsi-vector")]
    [InlineData("openfreemap", "openfreemap")]
    [InlineData("openstreetmap", "openstreetmap")]
    [InlineData("custom", "custom")]
    [InlineData("unknown", "gsi-pale")] // 未知キーは既定値
    [InlineData(null, "gsi-pale")]
    public void キーから選択肢を復元する(string? key, string expectedKey)
    {
        TileSourceChoice choice = TileSourceChoices.FromKey(key);
        Assert.Equal(expectedKey, choice.Key);
    }
}
