using PhotoMapStudio.App.Models;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.App.Tests.Models;

public sealed class CustomVectorChoiceTests
{
    [Fact]
    public void カスタムベクターはカタログの末尾に含まれ他のカスタムとは区別される()
    {
        TileSourceChoice choice = TileSourceChoices.CustomVector;

        Assert.Contains(choice, TileSourceChoices.All);
        Assert.Equal(TileSourceChoices.CustomVectorKey, choice.Key);
        Assert.True(choice.IsCustomVector);
        Assert.False(choice.IsCustom);
        Assert.Null(choice.Source);
        Assert.False(TileSourceChoices.Custom.IsCustomVector);
        Assert.True(TileSourceChoices.Custom.IsCustom);
        Assert.Equal(choice, TileSourceChoices.FromKey(TileSourceChoices.CustomVectorKey));
    }

    [Fact]
    public void カスタムベクターの選択肢から指定のタイルソースを構築する()
    {
        TileSource source = TileSourceChoices.CustomVector.CreateSource(
            "https://unused.example.com/{z}/{x}/{y}.png",
            "未使用",
            " https://example.com/japan.pmtiles ",
            VectorStyleSchema.GsiOptimalBvmap,
            " © 提供元 ",
            VectorSourceSpec.DefaultXyzMaxZoom);

        Assert.True(source.IsVector);
        Assert.Equal("© 提供元", source.Attribution);
        Assert.NotNull(source.VectorSource);
        Assert.Equal("https://example.com/japan.pmtiles", source.VectorSource.Location);
        Assert.Equal(VectorStyleSchema.GsiOptimalBvmap, source.VectorSource.Schema);
    }

    [Theory]
    [InlineData("", "© 提供元", "指定してください")]
    [InlineData("https://example.com/map.json", "© 提供元", ".pmtiles")]
    [InlineData("https://example.com/japan.pmtiles", "", "出典表示")]
    [InlineData("https://example.com/japan.pmtiles", "   ", "出典表示")]
    public void 指定が不正なカスタムベクターは理由つきで拒否する(string location, string attribution, string expectedMessagePart)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => TileSourceChoices.CustomVector.CreateSource(
                "https://unused.example.com/{z}/{x}/{y}.png", "未使用", location, VectorStyleSchema.OpenMapTiles, attribution, VectorSourceSpec.DefaultXyzMaxZoom));

        Assert.Contains(expectedMessagePart, exception.Message, StringComparison.Ordinal);

        // 画面に表示するため、英語の (Parameter ...) が付かない
        Assert.DoesNotContain("Parameter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ベクター以外の選択肢はベクター用の指定を使わない()
    {
        TileSource preset = TileSourceChoices.OpenFreeMap.CreateSource("https://x/{z}/{x}/{y}", "出典", string.Empty, VectorStyleSchema.OpenMapTiles, string.Empty, VectorSourceSpec.DefaultXyzMaxZoom);
        TileSource custom = TileSourceChoices.Custom.CreateSource("https://tiles.example.com/{z}/{x}/{y}.png", "出典", string.Empty, VectorStyleSchema.OpenMapTiles, string.Empty, VectorSourceSpec.DefaultXyzMaxZoom);

        Assert.Same(TileSources.OpenFreeMap, preset);
        Assert.False(custom.IsVector);
        Assert.Null(custom.VectorSource);
    }

    [Theory]
    [InlineData("https://example.com/{z}/{x}/{y}.pbf", 16, 16)]
    [InlineData("https://example.com/japan.pmtiles", 16, VectorSourceSpec.DefaultXyzMaxZoom)]
    public void URLテンプレートの最大ズームを指定のタイルソースへ反映する(string location, int maxZoom, int expected)
    {
        TileSource source = TileSourceChoices.CustomVector.CreateSource(
            "https://unused.example.com/{z}/{x}/{y}.png", "未使用", location, VectorStyleSchema.OpenMapTiles, "© 提供元", maxZoom);

        Assert.Equal(expected, source.VectorSource!.MaxZoom);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20)]
    public void URLテンプレートの最大ズームが範囲外なら理由つきで拒否する(int maxZoom)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => TileSourceChoices.CustomVector.CreateSource(
                "https://unused.example.com/{z}/{x}/{y}.png", "未使用", "https://example.com/{z}/{x}/{y}.pbf", VectorStyleSchema.OpenMapTiles, "© 提供元", maxZoom));

        Assert.Contains("最大ズーム", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void スキーマの選択肢は既定と変換を持つ()
    {
        Assert.Equal(VectorStyleSchema.OpenMapTiles, VectorSchemaChoice.Default.Schema);
        Assert.Equal(VectorStyleSchema.GsiOptimalBvmap, VectorSchemaChoice.FromSchema(VectorStyleSchema.GsiOptimalBvmap).Schema);
        Assert.Equal(VectorSchemaChoice.Default, VectorSchemaChoice.FromSchema((VectorStyleSchema)99));
        Assert.Equal(2, VectorSchemaChoice.All.Count);
    }

    [Fact]
    public void プレビュー設定もカスタムベクターからタイルソースを構築する()
    {
        var settings = new PreviewGenerationSettings
        {
            SelectedTileSource = TileSourceChoices.CustomVector,
            CustomVectorLocation = "https://example.com/{z}/{x}/{y}.pbf",
            CustomVectorSchema = VectorStyleSchema.OpenMapTiles,
            CustomVectorAttribution = "© 提供元",
            CustomVectorMaxZoom = 16,
        };

        TileSource source = settings.CreateTileSource();

        Assert.Equal(VectorSourceKind.XyzTemplate, source.VectorSource!.Kind);
        Assert.Equal(16, source.VectorSource.MaxZoom);
    }
}
