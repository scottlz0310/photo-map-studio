using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;

namespace PhotoMapStudio.App.Tests.ViewModels;

public class MainViewModelTests
{
    [Fact]
    public void 既定値を読み込む()
    {
        var repository = new InMemorySettingsRepository();
        var viewModel = new MainViewModel(repository);

        Assert.Equal(800, viewModel.Width);
        Assert.Equal(600, viewModel.Height);
        Assert.Equal(15, viewModel.Zoom);
        Assert.Same(TileSourceChoices.GsiPale, viewModel.SelectedTileSource);
        Assert.Equal(5, viewModel.MinimumZoom);
        Assert.Equal(18, viewModel.MaximumZoom);
    }

    [Fact]
    public void タイルソースの変更でズーム範囲と値を追従させる()
    {
        var viewModel = new MainViewModel(new InMemorySettingsRepository())
        {
            SelectedTileSource = TileSourceChoices.OpenStreetMap,
            Zoom = 3,
        };

        viewModel.SelectedTileSource = TileSourceChoices.GsiPale;

        Assert.Equal(5, viewModel.MinimumZoom);
        Assert.Equal(18, viewModel.MaximumZoom);
        Assert.Equal(5, viewModel.Zoom);
    }

    [Theory]
    [InlineData(0, 600, 15, "画像サイズ(幅・高さ)は正の整数を指定してください。")]
    [InlineData(800, 0, 15, "画像サイズ(幅・高さ)は正の整数を指定してください。")]
    [InlineData(800, 600, 0, "ズームレベルは 1 〜 19 の範囲で指定してください。")]
    [InlineData(800, 600, 20, "ズームレベルは 1 〜 19 の範囲で指定してください。")]
    public void 不正な設定は仕様順のメッセージで拒否する(
        double width,
        double height,
        double zoom,
        string expectedMessage)
    {
        var viewModel = new MainViewModel(new InMemorySettingsRepository())
        {
            Width = width,
            Height = height,
            Zoom = zoom,
        };

        Assert.False(viewModel.TrySaveSettings());
        Assert.Equal(expectedMessage, viewModel.ValidationMessage);
    }

    [Fact]
    public void 設定を保存して再読込できる()
    {
        var repository = new InMemorySettingsRepository();
        var viewModel = new MainViewModel(repository)
        {
            InputFolderPath = " C:\\Photos ",
            OutputFolderPath = " C:\\Maps ",
            Width = 1024,
            Height = 768,
            Zoom = 10,
            PinImagePath = " C:\\pin.png ",
            SelectedTileSource = TileSourceChoices.OpenStreetMap,
        };

        Assert.True(viewModel.TrySaveSettings());

        var reloaded = new MainViewModel(repository);

        Assert.Equal("C:\\Photos", reloaded.InputFolderPath);
        Assert.Equal("C:\\Maps", reloaded.OutputFolderPath);
        Assert.Equal(1024, reloaded.Width);
        Assert.Equal(768, reloaded.Height);
        Assert.Equal(10, reloaded.Zoom);
        Assert.Equal("C:\\pin.png", reloaded.PinImagePath);
        Assert.Equal(TileSourceChoices.OpenStreetMap, reloaded.SelectedTileSource);
        Assert.Equal("設定を保存しました。", viewModel.StatusMessage);
    }

    [Fact]
    public void カスタムタイルソースを検証して保存できる()
    {
        var repository = new InMemorySettingsRepository();
        var viewModel = new MainViewModel(repository)
        {
            SelectedTileSource = TileSourceChoices.Custom,
            CustomTileUrlTemplate = "https://tiles.example.com/{z}/{x}/{y}.png",
            CustomTileAttribution = "Example Maps",
            Zoom = 12,
        };

        Assert.True(viewModel.TrySaveSettings());
        Assert.Equal(TileSourceChoices.CustomKey, repository.Settings.TileSourceKey);
        Assert.Equal("https://tiles.example.com/{z}/{x}/{y}.png", repository.Settings.CustomTileUrlTemplate);
        Assert.Equal("Example Maps", repository.Settings.CustomTileAttribution);
    }

    private sealed class InMemorySettingsRepository : IPhotoMapSettingsRepository
    {
        public PhotoMapSettings Settings { get; set; } = new();

        public PhotoMapSettings Load() => this.Settings;

        public void Save(PhotoMapSettings settings) => this.Settings = settings;
    }
}
