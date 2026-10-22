using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.App.Models;
using PhotoMapStudio.Core.Maps;

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
    private const string TileCacheSizeMbKey = "tile-cache-size-mb";
    private const string PinKindKey = "pin-kind";
    private const string PinAnchorKey = "pin-anchor";
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
    {
        string pinImagePath = ReadString(PinImagePathKey, string.Empty);
        // 旧バージョンでカスタム画像を設定していた場合、新キー (pin-kind) が未保存なら CustomImage として自動移行する
        PinKind defaultKind = string.IsNullOrWhiteSpace(pinImagePath) ? PinKind.GreenPin : PinKind.CustomImage;
        PinKind pinKind = this.store.Read(PinKindKey) is null
            ? defaultKind
            : ReadEnum(PinKindKey, PinKind.GreenPin);

        return new()
        {
            InputFolderPath = ReadString(InputFolderPathKey, string.Empty),
            OutputFolderPath = ReadString(OutputFolderPathKey, string.Empty),
            IncludeSubfolders = this.store.Read("include-subfolders") is true,
            OutputFilePrefix = ReadString("output-file-prefix", string.Empty),
            OutputFilePostfix = ReadString("output-file-postfix", "_map"),
            Width = ReadInt(WidthKey, PhotoMapSettings.DefaultWidth),
            Height = ReadInt(HeightKey, PhotoMapSettings.DefaultHeight),
            Zoom = ReadInt(ZoomKey, PhotoMapSettings.DefaultZoom),
            TileCacheSizeMb = ReadInt(TileCacheSizeMbKey, PhotoMapSettings.DefaultTileCacheSizeMb),
            PinKind = pinKind,
            PinAnchor = ReadEnum(PinAnchorKey, PinAnchor.BottomCenter),
            PinImagePath = pinImagePath,
            TileSourceKey = ReadString(TileSourceKey, TileSourceChoices.GsiPaleKey),
            CustomTileUrlTemplate = ReadString(CustomTileUrlTemplateKey, PhotoMapSettings.DefaultCustomTileUrlTemplate),
            CustomTileAttribution = ReadString(CustomTileAttributionKey, PhotoMapSettings.DefaultCustomTileAttribution),
        };
    }

    /// <inheritdoc/>
    public void Save(PhotoMapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.store.Write(InputFolderPathKey, settings.InputFolderPath);
        this.store.Write(OutputFolderPathKey, settings.OutputFolderPath);
        this.store.Write("include-subfolders", settings.IncludeSubfolders);
        this.store.Write("output-file-prefix", settings.OutputFilePrefix);
        this.store.Write("output-file-postfix", settings.OutputFilePostfix);
        this.store.Write(WidthKey, settings.Width);
        this.store.Write(HeightKey, settings.Height);
        this.store.Write(ZoomKey, settings.Zoom);
        this.store.Write(TileCacheSizeMbKey, settings.TileCacheSizeMb);
        this.store.Write(PinKindKey, settings.PinKind.ToString());
        this.store.Write(PinAnchorKey, settings.PinAnchor.ToString());
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

    private T ReadEnum<T>(string key, T fallback) where T : struct, Enum
    {
        string text = ReadString(key, string.Empty);
        return Enum.TryParse<T>(text, ignoreCase: true, out T result) ? result : fallback;
    }
}
