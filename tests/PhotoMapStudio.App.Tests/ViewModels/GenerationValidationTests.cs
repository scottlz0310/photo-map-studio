using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.App.Tests.ViewModels;

public class GenerationValidationTests
{
    [Theory]
    [InlineData("..", "", "_map")]
    [InlineData("C:foo", "", "_map")]
    [InlineData("maps", "/", "_map")]
    [InlineData(".", "", "")]
    public async Task 相対出力と付加文字の検証エラーを表示して生成しない(string folder, string prefix, string postfix)
    {
        using var tree = new PhotoTree();
        var service = new Service();
        var model = new MainViewModel(new Repository(), batchGenerationService: service)
        { InputFolderPath = tree.Root, OutputFolderPath = folder, OutputFilePrefix = prefix, OutputFilePostfix = postfix };
        await model.GenerateCommand.ExecuteAsync(null);
        Assert.True(model.HasValidationError);
        Assert.False(service.Called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OSM公式を選択またはカスタム入力しても一括開始を拒否する(bool custom)
    {
        using var tree = new PhotoTree();
        var service = new Service();
        var model = new MainViewModel(new Repository(), batchGenerationService: service)
        {
            InputFolderPath = tree.Root,
            OutputFolderPath = "maps",
            SelectedTileSource = custom ? TileSourceChoices.Custom : TileSourceChoices.OpenStreetMap,
            CustomTileUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        };
        await model.GenerateCommand.ExecuteAsync(null);
        Assert.Contains("プレビュー用", model.ValidationMessage, StringComparison.Ordinal);
        Assert.False(service.Called);
        Assert.True(model.HasTileUsageMessage);
    }

    [Theory]
    [InlineData("osm")]
    [InlineData("custom")]
    public void 保存済みの配信元を復元した直後も利用条件の案内を表示する(string key)
    {
        var source = key == "osm" ? TileSourceChoices.OpenStreetMap : TileSourceChoices.Custom;
        var model = new MainViewModel(new Repository(new() { TileSourceKey = source.Key }));
        Assert.True(model.HasTileUsageMessage);
        Assert.NotEmpty(model.TileUsageMessage);
    }

    private sealed class Repository(PhotoMapSettings? settings = null) : IPhotoMapSettingsRepository
    {
        public PhotoMapSettings Load() => settings ?? new();
        public void Save(PhotoMapSettings value) { }
    }
    private sealed class Service : IBatchGenerationService
    {
        public bool Called { get; private set; }
        public Task<BatchGenerationSummary> GenerateAsync(BatchGenerationSettings settings, IProgress<BatchGenerationProgress>? progress, CancellationToken cancellationToken)
        { this.Called = true; return Task.FromResult(new BatchGenerationSummary(0, 0, 0, false)); }
    }
}
