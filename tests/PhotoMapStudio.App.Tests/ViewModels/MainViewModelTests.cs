using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;
using PhotoMapStudio.Core.Maps;

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
        Assert.Equal(PinKind.GreenPin, viewModel.SelectedPinKindChoice.Kind);
        Assert.Equal(PinAnchor.BottomCenter, viewModel.SelectedPinAnchorChoice.Anchor);
        Assert.False(viewModel.IsCustomPinImageVisible);
        Assert.Equal(5, viewModel.MinimumZoom);
        Assert.Equal(18, viewModel.MaximumZoom);
    }

    [Fact]
    public void ピン形状をCustomImageに変更するとカスタム画像欄の可視性がtrueになる()
    {
        var viewModel = new MainViewModel(new InMemorySettingsRepository());
        Assert.False(viewModel.IsCustomPinImageVisible);

        viewModel.SelectedPinKindChoice = PinKindChoice.FromKind(PinKind.CustomImage);
        Assert.True(viewModel.IsCustomPinImageVisible);

        viewModel.SelectedPinKindChoice = PinKindChoice.FromKind(PinKind.Crosshair);
        Assert.False(viewModel.IsCustomPinImageVisible);
    }

    [Fact]
    public void カスタム画像で中央アンカー選択後に同梱ピンへ戻すと下端中央にリセットされる()
    {
        var repository = new InMemorySettingsRepository();
        var viewModel = new MainViewModel(repository);

        // 1. カスタム画像を選択し、中央アンカーを指定
        viewModel.SelectedPinKindChoice = PinKindChoice.FromKind(PinKind.CustomImage);
        viewModel.SelectedPinAnchorChoice = PinAnchorChoice.FromAnchor(PinAnchor.Center);
        viewModel.PinImagePath = "C:\\pin.png";

        Assert.Equal(PinAnchor.Center, viewModel.SelectedPinAnchorChoice.Anchor);

        // 2. 同梱ピン（赤ピン）へ切り替える
        viewModel.SelectedPinKindChoice = PinKindChoice.FromKind(PinKind.RedPin);

        // アンカー選択が既定値（下端中央）に戻り、カスタム欄が非表示になること
        Assert.Equal(PinAnchor.BottomCenter, viewModel.SelectedPinAnchorChoice.Anchor);
        Assert.False(viewModel.IsCustomPinImageVisible);

        // 設定保存時も BottomCenter が永続化されること
        Assert.True(viewModel.TrySaveSettings());
        Assert.Equal(PinKind.RedPin, repository.Settings.PinKind);
        Assert.Equal(PinAnchor.BottomCenter, repository.Settings.PinAnchor);
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
            SelectedPinKindChoice = PinKindChoice.FromKind(PinKind.CustomImage),
            SelectedPinAnchorChoice = PinAnchorChoice.FromAnchor(PinAnchor.Center),
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
        Assert.Equal(PinKind.CustomImage, reloaded.SelectedPinKindChoice.Kind);
        Assert.Equal(PinAnchor.Center, reloaded.SelectedPinAnchorChoice.Anchor);
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
