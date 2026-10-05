using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;

namespace PhotoMapStudio.App.Tests.Services;

public class PhotoMapSettingsRepositoryTests
{
    [Theory]
    [InlineData(false, "", "_map")]
    [InlineData(true, "map_", "_z16")]
    [InlineData(true, "", "")]
    public void 再帰と付加文字を保存し明示的な空の末尾文字を維持する(bool recursive, string prefix, string postfix)
    {
        var repository = new PhotoMapSettingsRepository(new InMemorySettingsValueStore());
        var settings = new PhotoMapSettings { IncludeSubfolders = recursive, OutputFilePrefix = prefix, OutputFilePostfix = postfix };
        repository.Save(settings);
        Assert.Equal(settings, repository.Load());
    }

    [Fact]
    public void 旧保存値にキーがない場合は既定値を復元する()
    {
        var settings = new PhotoMapSettingsRepository(new InMemorySettingsValueStore()).Load();
        Assert.False(settings.IncludeSubfolders);
        Assert.Empty(settings.OutputFilePrefix);
        Assert.Equal("_map", settings.OutputFilePostfix);
    }

    [Fact]
    public void キーバリューストアへ設定を保存して復元する()
    {
        var store = new InMemorySettingsValueStore();
        var repository = new PhotoMapSettingsRepository(store);
        var settings = new PhotoMapSettings
        {
            InputFolderPath = "C:\\Photos",
            OutputFolderPath = "C:\\Maps",
            Width = 1280,
            Height = 720,
            Zoom = 12,
            PinImagePath = "C:\\pin.png",
            TileSourceKey = TileSourceChoices.CustomKey,
            CustomTileUrlTemplate = "https://tiles.example.com/{z}/{x}/{y}.png",
            CustomTileAttribution = "Example Maps",
        };

        repository.Save(settings);

        Assert.Equal(settings, repository.Load());
    }

    [Fact]
    public void ピン画像が空の場合は保存値を削除する()
    {
        var store = new InMemorySettingsValueStore();
        var repository = new PhotoMapSettingsRepository(store);

        repository.Save(new PhotoMapSettings { PinImagePath = "C:\\pin.png" });
        repository.Save(new PhotoMapSettings());

        Assert.Equal(string.Empty, repository.Load().PinImagePath);
    }

    private sealed class InMemorySettingsValueStore : ISettingsValueStore
    {
        private readonly Dictionary<string, object> values = [];

        public object? Read(string key)
            => this.values.TryGetValue(key, out object? value) ? value : null;

        public void Write(string key, object value)
            => this.values[key] = value;

        public void Remove(string key)
            => this.values.Remove(key);
    }
}
