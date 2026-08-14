using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// キー・バリュー ストアへ PhotoMapStudio の設定を保存する。
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI コンテナーから生成される設定リポジトリ。")]
internal sealed class PhotoMapSettingsRepository : IPhotoMapSettingsRepository
{
    private const string InputFolderPathKey = "input-folder-path";
    private const string OutputFolderPathKey = "output-folder-path";
    private const string WidthKey = "width";
    private const string HeightKey = "height";
    private const string ZoomKey = "zoom";
    private const string PinImagePathKey = "pin-image-path";
    private const string TileSourceKey = "tile-source-key";
    private const string CustomTileUrlTemplateKey = "custom-tile-url-template";
    private const string CustomTileAttributionKey = "custom-tile-attribution";

    private readonly ISettingsValueStore store;

    /// <summary>
    /// リポジトリを構築する。
    /// </summary>
    /// <param name="store">設定ストア。</param>
    public PhotoMapSettingsRepository(ISettingsValueStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public PhotoMapSettings Load()
        => new()
        {
            InputFolderPath = ReadString(InputFolderPathKey, string.Empty),
            OutputFolderPath = ReadString(OutputFolderPathKey, string.Empty),
            Width = ReadInt(WidthKey, PhotoMapSettings.DefaultWidth),
            Height = ReadInt(HeightKey, PhotoMapSettings.DefaultHeight),
            Zoom = ReadInt(ZoomKey, PhotoMapSettings.DefaultZoom),
            PinImagePath = ReadString(PinImagePathKey, string.Empty),
            TileSourceKey = ReadString(TileSourceKey, TileSourceChoices.GsiPaleKey),
            CustomTileUrlTemplate = ReadString(CustomTileUrlTemplateKey, PhotoMapSettings.DefaultCustomTileUrlTemplate),
            CustomTileAttribution = ReadString(CustomTileAttributionKey, PhotoMapSettings.DefaultCustomTileAttribution),
        };

    /// <inheritdoc/>
    public void Save(PhotoMapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.store.Write(InputFolderPathKey, settings.InputFolderPath);
        this.store.Write(OutputFolderPathKey, settings.OutputFolderPath);
        this.store.Write(WidthKey, settings.Width);
        this.store.Write(HeightKey, settings.Height);
        this.store.Write(ZoomKey, settings.Zoom);
        this.store.Write(TileSourceKey, settings.TileSourceKey);
        this.store.Write(CustomTileUrlTemplateKey, settings.CustomTileUrlTemplate);
        this.store.Write(CustomTileAttributionKey, settings.CustomTileAttribution);

        if (string.IsNullOrWhiteSpace(settings.PinImagePath))
        {
            this.store.Remove(PinImagePathKey);
        }
        else
        {
            this.store.Write(PinImagePathKey, settings.PinImagePath);
        }
    }

    private string ReadString(string key, string fallback)
        => this.store.Read(key) is string value ? value : fallback;

    private int ReadInt(string key, int fallback)
        => this.store.Read(key) is int value ? value : fallback;
}
