using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;

namespace PhotoMapStudio.App.Tests.Services;

public class OutputPathResolverTests
{
    [Theory]
    [InlineData(@"D:\Maps", @"D:\Maps\a_map.png")]
    [InlineData(".", @"C:\Photos\2026\a_map.png")]
    [InlineData("maps", @"C:\Photos\2026\maps\a_map.png")]
    [InlineData(@"out\map", @"C:\Photos\2026\out\map\a_map.png")]
    [InlineData(@"\\server\share", @"\\server\share\a_map.png")]
    public void 絶対相対UNCを写真フォルダ基準で解決する(string output, string expected)
    {
        OutputPathResolver.Validate(output, "", "_map");
        Assert.Equal(expected, OutputPathResolver.Resolve(@"C:\Photos\2026\a.jpg", new() { OutputFolderPath = output }));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\x")]
    [InlineData(@"a\..\..\x")]
    [InlineData(@"\foo")]
    [InlineData("C:foo")]
    [InlineData("bad\0path")]
    [InlineData("bad?path")]
    [InlineData("bad*path")]
    public void 曖昧または外へ出る相対指定を拒否する(string output)
        => Assert.ThrowsAny<ArgumentException>(() => OutputPathResolver.Validate(output, "", "_map"));

    [Theory]
    [InlineData("", "_map", "a_map.png")]
    [InlineData("map_", "", "map_a.png")]
    [InlineData("", "_z16", "a_z16.png")]
    [InlineData("map_", "_z16", "map_a_z16.png")]
    [InlineData("", "", "a.png")]
    public void 付加文字を適用してPNG名を作る(string prefix, string postfix, string expected)
    {
        OutputPathResolver.Validate("maps", prefix, postfix);
        Assert.Equal(expected, Path.GetFileName(OutputPathResolver.Resolve(@"C:\Photos\a.jpg", new()
        { OutputFolderPath = "maps", OutputFilePrefix = prefix, OutputFilePostfix = postfix })));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData(":")]
    [InlineData("*")]
    [InlineData("?")]
    [InlineData("\"")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("|")]
    [InlineData("\u0001")]
    public void 付加文字のパス区切りと無効文字を拒否する(string invalid)
    {
        Assert.Throws<ArgumentException>(() => OutputPathResolver.Validate("maps", invalid, ""));
        Assert.Throws<ArgumentException>(() => OutputPathResolver.Validate("maps", "", invalid));
    }

    [Theory]
    [InlineData(".")]
    [InlineData(@"maps\..")]
    public void 元写真と同じ場所では両方空の付加文字を拒否する(string output)
        => Assert.Throws<ArgumentException>(() => OutputPathResolver.Validate(output, "", ""));

    [Theory]
    [InlineData(251, false)]
    [InlineData(252, true)]
    public void 最終ファイル名の二百五十五文字上限を判定する(int stemLength, bool invalid)
    {
        var settings = new BatchGenerationSettings { OutputFolderPath = "maps", OutputFilePostfix = "" };
        string photo = Path.Combine(@"C:\Photos", new string('a', stemLength) + ".jpg");
        if (invalid) { Assert.Throws<ArgumentException>(() => OutputPathResolver.Resolve(photo, settings)); }
        else { Assert.Equal(255, Path.GetFileName(OutputPathResolver.Resolve(photo, settings)).Length); }
    }
}
