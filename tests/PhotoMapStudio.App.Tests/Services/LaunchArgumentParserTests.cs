using PhotoMapStudio.App.Services;

namespace PhotoMapStudio.App.Tests.Services;

public class LaunchArgumentParserTests
{
    [Fact]
    public void 引用符付きのフォルダ引数を解析する()
    {
        LaunchArguments result = LaunchArgumentParser.Parse(
            "--input-dir \"C:\\Photo Folder\" --output-dir=\"D:\\Map Folder\"");

        Assert.Equal("C:\\Photo Folder", result.InputDirectoryPath);
        Assert.Equal("D:\\Map Folder", result.OutputDirectoryPath);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void 値のない引数をエラーにする()
    {
        LaunchArguments result = LaunchArgumentParser.Parse("--input-dir --output-dir C:\\Maps");

        Assert.Null(result.InputDirectoryPath);
        Assert.Equal("C:\\Maps", result.OutputDirectoryPath);
        Assert.Contains("--input-dir", Assert.Single(result.Errors), StringComparison.Ordinal);
    }
}
