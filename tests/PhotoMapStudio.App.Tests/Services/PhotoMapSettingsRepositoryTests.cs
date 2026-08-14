using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;

namespace PhotoMapStudio.App.Tests.Services;

public class PhotoMapSettingsRepositoryTests
{
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
