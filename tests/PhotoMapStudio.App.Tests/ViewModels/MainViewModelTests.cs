using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

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
        Assert.Equal(1024, viewModel.TileCacheSizeMb);
        Assert.Same(TileSourceChoices.GsiPale, viewModel.SelectedTileSource);
        Assert.Equal(PinKind.GreenPin, viewModel.SelectedPinKindChoice.Kind);
        Assert.Equal(PinAnchor.BottomCenter, viewModel.SelectedPinAnchorChoice.Anchor);
        Assert.False(viewModel.IsCustomPinImageVisible);
        Assert.Equal(5, viewModel.MinimumZoom);
        Assert.Equal(18, viewModel.MaximumZoom);
    }

    [Fact]
    public void タイルキャッシュ設定と消去コマンドが動作する()
    {
        var repository = new InMemorySettingsRepository();
        var stubCache = new StubTileCache();
        var viewModel = new MainViewModel(repository, tileCache: stubCache);

        Assert.Equal(1024L * 1024L * 1024L, stubCache.MaxSizeBytes);
        Assert.Contains("10.0 MB / 1024 MB", viewModel.TileCacheUsageText, StringComparison.Ordinal);

        // 容量変更
        viewModel.TileCacheSizeMb = 512;
        Assert.Equal(512L * 1024L * 1024L, stubCache.MaxSizeBytes);
        Assert.Contains("10.0 MB / 512 MB", viewModel.TileCacheUsageText, StringComparison.Ordinal);

        // キャッシュ消去
        viewModel.ClearTileCacheCommand.Execute(null);
        Assert.True(stubCache.ClearCalled);
        Assert.Contains("0.0 MB / 512 MB", viewModel.TileCacheUsageText, StringComparison.Ordinal);
        Assert.Equal("タイルキャッシュを消去しました。", viewModel.StatusMessage);
    }

    [Fact]
    public void タイルキャッシュ消去失敗時に警告が表示される()
    {
        var repository = new InMemorySettingsRepository();
        var stubCache = new StubTileCache { ClearResult = false };
        var viewModel = new MainViewModel(repository, tileCache: stubCache);

        viewModel.ClearTileCacheCommand.Execute(null);
        Assert.True(stubCache.ClearCalled);
        Assert.Contains("一部のキャッシュファイルを消去できませんでした", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("10.0 MB / 1024 MB", viewModel.TileCacheUsageText, StringComparison.Ordinal);
    }

    [Fact]
    public void タイルキャッシュ容量変更時にPruneAsyncが呼ばれ使用量が更新される()
    {
        var repository = new InMemorySettingsRepository();
        var stubCache = new StubTileCache { Size = 1000L * 1024L * 1024L };
        var viewModel = new MainViewModel(repository, tileCache: stubCache);

        viewModel.TileCacheSizeMb = 100;

        Assert.Equal(100L * 1024L * 1024L, stubCache.LastPrunedTarget);
        Assert.Equal(100L * 1024L * 1024L, stubCache.MaxSizeBytes);
        Assert.Contains("100.0 MB / 100 MB", viewModel.TileCacheUsageText, StringComparison.Ordinal);
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
    [InlineData(0, 600, 15, 1024, "画像サイズ(幅・高さ)は正の整数を指定してください。")]
    [InlineData(800, 0, 15, 1024, "画像サイズ(幅・高さ)は正の整数を指定してください。")]
    [InlineData(800, 600, 0, 1024, "ズームレベルは 1 〜 19 の範囲で指定してください。")]
    [InlineData(800, 600, 20, 1024, "ズームレベルは 1 〜 19 の範囲で指定してください。")]
    [InlineData(800, 600, 15, 0, "キャッシュ容量上限は正の整数を指定してください。")]
    [InlineData(800, 600, 15, -100, "キャッシュ容量上限は正の整数を指定してください。")]
    public void 不正な設定は仕様順のメッセージで拒否する(
        double width,
        double height,
        double zoom,
        double cacheSizeMb,
        string expectedMessage)
    {
        var viewModel = new MainViewModel(new InMemorySettingsRepository())
        {
            Width = width,
            Height = height,
            Zoom = zoom,
            TileCacheSizeMb = cacheSizeMb,
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
            TileCacheSizeMb = 2048,
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
        Assert.Equal(2048, reloaded.TileCacheSizeMb);
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

    [Fact]
    public async Task 保存済み容量2GBで1GB超の既存キャッシュを保持したまま再起動できる()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "pms_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // 1GB超の既存キャッシュファイルをシミュレート
            string testFile = Path.Combine(tempDir, "cached_tile.pbf");
            await File.WriteAllBytesAsync(testFile, new byte[1200]);

            // DI登録順序: 既定1GBの FileSystemTileCache が先にインスタンス化される
            using var tileCache = new FileSystemTileCache(tempDir);

            // DI直後: まだ設定ロード前なので勝手に削除されない
            Assert.Equal(1200, tileCache.GetCurrentSizeBytes());

            // 設定リポジトリ: 2048MB が保存されている
            var repository = new InMemorySettingsRepository
            {
                Settings = new PhotoMapSettings { TileCacheSizeMb = 2048 }
            };

            // MainViewModel の初期化: 設定から 2048MB をロードして tileCache に適用
            var viewModel = new MainViewModel(repository, tileCache: tileCache);

            Assert.Equal(2048L * 1024L * 1024L, tileCache.MaxSizeBytes);
            Assert.True(File.Exists(testFile));
            Assert.Equal(1200, tileCache.GetCurrentSizeBytes());
            Assert.Contains("/ 2048 MB", viewModel.TileCacheUsageText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class InMemorySettingsRepository : IPhotoMapSettingsRepository
    {
        public PhotoMapSettings Settings { get; set; } = new();

        public PhotoMapSettings Load() => this.Settings;

        public void Save(PhotoMapSettings settings) => this.Settings = settings;
    }

    private sealed class StubTileCache : ITileCache
    {
        public long MaxSizeBytes { get; set; }
        public long Size { get; set; } = 10L * 1024L * 1024L;
        public bool ClearCalled { get; private set; }
        public bool ClearResult { get; set; } = true;
        public long? LastPrunedTarget { get; private set; }

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken) => Task.CompletedTask;
        public long GetCurrentSizeBytes() => this.ClearCalled && this.ClearResult ? 0 : this.Size;
        public Task<bool> ClearAsync(CancellationToken cancellationToken = default)
        {
            this.ClearCalled = true;
            return Task.FromResult(this.ClearResult);
        }
        public Task PruneAsync(long targetSizeBytes, CancellationToken cancellationToken = default)
        {
            this.LastPrunedTarget = targetSizeBytes;
            if (this.Size > targetSizeBytes)
            {
                this.Size = targetSizeBytes;
            }
            return Task.CompletedTask;
        }
    }
}
