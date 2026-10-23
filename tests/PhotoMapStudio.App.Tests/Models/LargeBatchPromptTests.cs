using PhotoMapStudio.App.Models;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.App.Tests.Models;

public sealed class LargeBatchPromptTests
{
    [Theory]
    [InlineData("gsi-pale", 5000, false)]
    [InlineData("gsi-vector", 5000, false)]
    [InlineData("openfreemap", 100, false)]
    [InlineData("openfreemap", 101, true)]
    [InlineData("custom-raster", 100, false)]
    [InlineData("custom-raster", 101, true)]
    [InlineData("custom-loopback", 5000, false)]
    [InlineData("custom-vector-url", 101, true)]
    [InlineData("custom-vector-loopback", 5000, false)]
    public void 確認の要否は配信元と枚数で決まる(string kind, int count, bool expected)
    {
        TileSource source = CreateSource(kind);

        Assert.Equal(expected, LargeBatchPrompt.IsRequired(source, count));
    }

    [Fact]
    public void ローカルのPMTilesファイルは枚数によらず確認しない()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pms-prompt-{Guid.NewGuid():N}.pmtiles");
        File.WriteAllBytes(path, [0]);
        try
        {
            TileSource source = TileSources.CustomVector(VectorSourceSpec.Create(path, VectorStyleSchema.OpenMapTiles), "© 検証");

            Assert.False(LargeBatchPrompt.IsRequired(source, 5000));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("openfreemap", "OpenFreeMap で150枚を一括生成", "3,000 タイル", "規約を確認して開始")]
    [InlineData("custom-raster", "カスタム配信元で150枚を一括生成", "1 秒間隔", "許可を確認して開始")]
    [InlineData("custom-vector-url", "カスタム（ベクター）の配信元で150枚を一括生成", "125 ミリ秒", "許可を確認して開始")]
    public void 確認の文言は配信元ごとの実際の取得方式を示す(string kind, string expectedTitle, string expectedMessagePart, string expectedButton)
    {
        LargeBatchPrompt prompt = LargeBatchPrompt.Create(CreateSource(kind), 150);

        Assert.Equal(150, prompt.Count);
        Assert.Equal(expectedTitle, prompt.Title);
        Assert.Contains(expectedMessagePart, prompt.Message, StringComparison.Ordinal);
        Assert.Equal(expectedButton, prompt.ConfirmButtonText);
    }

    [Fact]
    public void OpenFreeMapの確認はカスタム配信元と書かない()
    {
        LargeBatchPrompt prompt = LargeBatchPrompt.Create(TileSources.OpenFreeMap, 150);

        Assert.DoesNotContain("カスタム", prompt.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("1 秒間隔", prompt.Message, StringComparison.Ordinal);
    }

    private static TileSource CreateSource(string kind) => kind switch
    {
        "gsi-pale" => TileSources.GsiPale,
        "gsi-vector" => TileSources.GsiVector,
        "openfreemap" => TileSources.OpenFreeMap,
        "custom-raster" => TileSources.Custom("https://tiles.example.com/{z}/{x}/{y}.png", "出典"),
        "custom-loopback" => TileSources.Custom("http://127.0.0.1:8765/{z}/{x}/{y}.png", "出典"),
        "custom-vector-url" => TileSources.CustomVector(
            VectorSourceSpec.Create("https://tiles.example.com/japan.pmtiles", VectorStyleSchema.OpenMapTiles), "© 検証"),
        "custom-vector-loopback" => TileSources.CustomVector(
            VectorSourceSpec.Create("http://127.0.0.1:8765/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles), "© 検証"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
