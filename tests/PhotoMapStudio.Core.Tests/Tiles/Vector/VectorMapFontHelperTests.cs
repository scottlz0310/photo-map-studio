using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

using Xunit;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public sealed class VectorMapFontHelperTests
{
    [Fact]
    public void CreateJapaneseTypefaceReturnsTypefaceWithJapaneseGlyphs()
    {
        using var typeface = VectorMapFontHelper.CreateJapaneseTypeface();
        Assert.NotNull(typeface);

        using var font = new SKFont(typeface, 12f);
        Assert.True(font.ContainsGlyphs("東京駅富士山京都名古屋"));
    }

    [Fact]
    public void GsiVectorStyleInitializesWithJapaneseCapableFont()
    {
        using var style = new GsiVectorStyle();
        Assert.Equal("gsi", style.StyleId);
    }

    [Fact]
    public void OpenMapTilesStyleInitializesWithJapaneseCapableFont()
    {
        using var style = new OpenMapTilesStyle();
        Assert.Equal("openmaptiles", style.StyleId);
    }
}
