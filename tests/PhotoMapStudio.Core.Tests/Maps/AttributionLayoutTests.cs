using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Maps;

public class AttributionLayoutTests
{
    private const string GsiAttribution = "国土地理院（https://maps.gsi.go.jp/development/ichiran.html）";

    [Theory]
    [InlineData(GsiAttribution, 92)]
    [InlineData("© OpenStreetMap contributors", 92)]
    [InlineData(GsiAttribution, 200)]
    public void 折り返した各行は指定幅に収まる(string text, float maxWidth)
    {
        using SKFont font = CreateFont(6f);

        IReadOnlyList<string> lines = AttributionLayout.Wrap(font, text, maxWidth);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(
            font.MeasureText(line) <= maxWidth,
            $"行が幅を超えています: {line} ({font.MeasureText(line)} > {maxWidth})"));
    }

    [Theory]
    [InlineData(GsiAttribution, 92)]
    [InlineData("© OpenStreetMap contributors", 40)]
    public void 折り返しても文字列は欠落しない(string text, float maxWidth)
    {
        using SKFont font = CreateFont(6f);

        IReadOnlyList<string> lines = AttributionLayout.Wrap(font, text, maxWidth);

        Assert.Equal(text, string.Concat(lines));
    }

    [Fact]
    public void 一行に収まる場合は分割しない()
    {
        using SKFont font = CreateFont(10f);
        const string Text = "© OpenStreetMap contributors";

        IReadOnlyList<string> lines = AttributionLayout.Wrap(font, Text, 1000f);

        Assert.Equal([Text], lines);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(0.5f)]
    public void 一文字も収まらない幅では空を返す(float maxWidth)
    {
        using SKFont font = CreateFont(6f);

        Assert.Empty(AttributionLayout.Wrap(font, GsiAttribution, maxWidth));
    }

    [Fact]
    public void 空文字列では空を返す()
    {
        using SKFont font = CreateFont(6f);

        Assert.Empty(AttributionLayout.Wrap(font, string.Empty, 100f));
    }

    [Fact]
    public void サロゲートペアを分断しない()
    {
        using SKFont font = CreateFont(6f);
        string text = string.Concat(Enumerable.Repeat("\U0001F5FE", 20));

        IReadOnlyList<string> lines = AttributionLayout.Wrap(font, text, 20f);

        Assert.Equal(text, string.Concat(lines));
        Assert.All(lines, line =>
        {
            Assert.False(char.IsHighSurrogate(line[^1]), $"行末がサロゲートの前半で切れています: {line}");
            Assert.False(char.IsLowSurrogate(line[0]), $"行頭がサロゲートの後半になっています: {line}");
        });
    }

    [Fact]
    public void プリセットの出典はいずれも折り返せる()
    {
        using SKFont font = CreateFont(6f);

        Assert.All(TileSources.All, source
            => Assert.NotEmpty(AttributionLayout.Wrap(font, source.Attribution, 92f)));
    }

    private static SKFont CreateFont(float size)
        => new(SKFontManager.Default.MatchCharacter('国') ?? SKTypeface.Default, size);
}
