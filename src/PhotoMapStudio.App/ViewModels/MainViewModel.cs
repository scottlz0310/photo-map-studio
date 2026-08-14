using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.ViewModels;

/// <summary>
/// メイン画面の設定状態を管理する ViewModel。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1056:URI プロパティは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML バインディングと App.Tests の差し替え可能な ViewModel 契約として公開する。")]
public sealed class MainViewModel : ObservableObject
{
    private const string InvalidImageSizeMessage = "画像サイズ(幅・高さ)は正の整数を指定してください。";
    private const string InvalidZoomMessage = "ズームレベルは 1 〜 19 の範囲で指定してください。";

    private readonly IPhotoMapSettingsRepository settingsRepository;
    private string inputFolderPath;
    private string outputFolderPath;
    private double width;
    private double height;
    private double zoom;
    private string pinImagePath;
    private TileSourceChoice selectedTileSource;
    private string customTileUrlTemplate;
    private string customTileAttribution;
    private double minimumZoom;
    private double maximumZoom;
    private string validationMessage = string.Empty;
    private string statusMessage = string.Empty;

    /// <summary>
    /// ViewModel を構築する。
    /// </summary>
    /// <param name="settingsRepository">設定リポジトリ。</param>
    public MainViewModel(IPhotoMapSettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));

        var settings = this.settingsRepository.Load();
        this.inputFolderPath = settings.InputFolderPath;
        this.outputFolderPath = settings.OutputFolderPath;
        this.width = settings.Width;
        this.height = settings.Height;
        this.pinImagePath = settings.PinImagePath;
        this.customTileUrlTemplate = settings.CustomTileUrlTemplate;
        this.customTileAttribution = settings.CustomTileAttribution;
        this.selectedTileSource = PhotoMapStudio.App.Models.TileSourceChoices.FromKey(settings.TileSourceKey);
        this.minimumZoom = this.selectedTileSource.MinZoom;
        this.maximumZoom = this.selectedTileSource.MaxZoom;
        this.zoom = Math.Clamp(settings.Zoom, this.minimumZoom, this.maximumZoom);

        this.TileSourceOptions = PhotoMapStudio.App.Models.TileSourceChoices.All;
        this.SaveSettingsCommand = new RelayCommand(this.SaveSettings);
    }

    /// <summary>入力フォルダ。</summary>
    public string InputFolderPath
    {
        get => this.inputFolderPath;
        set
        {
            if (this.SetProperty(ref this.inputFolderPath, value ?? string.Empty))
            {
                this.ClearFeedback();
            }
        }
    }

    /// <summary>出力フォルダ。</summary>
    public string OutputFolderPath
    {
        get => this.outputFolderPath;
        set
        {
            if (this.SetProperty(ref this.outputFolderPath, value ?? string.Empty))
            {
                this.ClearFeedback();
            }
        }
    }

    /// <summary>出力幅（ピクセル）。</summary>
    public double Width
    {
        get => this.width;
        set
        {
            if (this.SetProperty(ref this.width, value))
            {
                this.ClearFeedback();
            }
        }
    }

    /// <summary>出力高さ（ピクセル）。</summary>
    public double Height
    {
        get => this.height;
        set
        {
            if (this.SetProperty(ref this.height, value))
            {
                this.ClearFeedback();
            }
        }
    }

    /// <summary>ズームレベル。</summary>
    public double Zoom
    {
        get => this.zoom;
        set
        {
            if (this.SetProperty(ref this.zoom, value))
            {
                this.ClearFeedback();
            }
        }
    }

    /// <summary>ピン画像のパス。</summary>
    public string PinImagePath
    {
        get => this.pinImagePath;
        set
        {
            if (this.SetProperty(ref this.pinImagePath, value ?? string.Empty))
            {
                this.ClearFeedback();
            }
        }
    }

    /// <summary>選択中のタイルソース。</summary>
    public TileSourceChoice SelectedTileSource
    {
        get => this.selectedTileSource;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!this.SetProperty(ref this.selectedTileSource, value))
            {
                return;
            }

            this.MinimumZoom = value.MinZoom;
            this.MaximumZoom = value.MaxZoom;
            this.Zoom = Math.Clamp(this.Zoom, this.MinimumZoom, this.MaximumZoom);
            this.OnPropertyChanged(nameof(this.IsCustomTileSource));
            this.OnPropertyChanged(nameof(this.SelectedTileSourceAttribution));
            this.ClearFeedback();
        }
    }

    /// <summary>カスタムタイル URL テンプレート。</summary>
    public string CustomTileUrlTemplate
    {
        get => this.customTileUrlTemplate;
        set
        {
            if (this.SetProperty(ref this.customTileUrlTemplate, value ?? string.Empty))
            {
                this.ClearFeedback();
            }
        }
    }

    /// <summary>カスタムタイルの出典表示。</summary>
    public string CustomTileAttribution
    {
        get => this.customTileAttribution;
        set
        {
            if (this.SetProperty(ref this.customTileAttribution, value ?? string.Empty))
            {
                this.ClearFeedback();
                this.OnPropertyChanged(nameof(this.SelectedTileSourceAttribution));
            }
        }
    }

    /// <summary>現在選択中のタイルソースで許可される最小ズーム。</summary>
    public double MinimumZoom
    {
        get => this.minimumZoom;
        private set => this.SetProperty(ref this.minimumZoom, value);
    }

    /// <summary>現在選択中のタイルソースで許可される最大ズーム。</summary>
    public double MaximumZoom
    {
        get => this.maximumZoom;
        private set => this.SetProperty(ref this.maximumZoom, value);
    }

    /// <summary>カスタム URL が選択されているかどうか。</summary>
    public bool IsCustomTileSource => this.SelectedTileSource.IsCustom;

    /// <summary>現在選択中のタイルソースの出典表示。</summary>
    public string SelectedTileSourceAttribution
        => this.SelectedTileSource.IsCustom
            ? this.CustomTileAttribution
            : this.SelectedTileSource.Source!.Attribution;

    /// <summary>タイルソースの選択肢。</summary>
    public IReadOnlyList<TileSourceChoice> TileSourceOptions { get; }

    /// <summary>入力値の検証エラー。</summary>
    public string ValidationMessage
    {
        get => this.validationMessage;
        private set
        {
            if (this.SetProperty(ref this.validationMessage, value))
            {
                this.OnPropertyChanged(nameof(this.HasValidationError));
            }
        }
    }

    /// <summary>直近の保存結果。</summary>
    public string StatusMessage
    {
        get => this.statusMessage;
        private set => this.SetProperty(ref this.statusMessage, value);
    }

    /// <summary>検証エラーがあるかどうか。</summary>
    public bool HasValidationError => !string.IsNullOrEmpty(this.ValidationMessage);

    /// <summary>設定保存コマンド。</summary>
    public ICommand SaveSettingsCommand { get; }

    /// <summary>
    /// 現在の入力を検証して設定を保存する。
    /// </summary>
    /// <returns>保存できた場合は <see langword="true"/>。</returns>
    public bool TrySaveSettings()
    {
        this.ClearFeedback();

        if (!TryGetPositiveInteger(this.Width, out int width)
            || !TryGetPositiveInteger(this.Height, out int height))
        {
            this.ValidationMessage = InvalidImageSizeMessage;
            return false;
        }

        if (!TryGetInteger(this.Zoom, out int zoom) || zoom is < 1 or > 19)
        {
            this.ValidationMessage = InvalidZoomMessage;
            return false;
        }

        TileSource tileSource;
        try
        {
            tileSource = this.SelectedTileSource.CreateSource(
                this.CustomTileUrlTemplate,
                this.CustomTileAttribution);
        }
        catch (ArgumentException exception)
        {
            this.ValidationMessage = $"カスタムタイルソースを検証できません: {exception.Message}";
            return false;
        }

        if (!tileSource.SupportsZoom(zoom))
        {
            this.ValidationMessage = $"選択中のタイルソースではズームレベルは {tileSource.MinZoom} 〜 {tileSource.MaxZoom} の範囲で指定してください。";
            return false;
        }

        this.settingsRepository.Save(new PhotoMapSettings
        {
            InputFolderPath = this.InputFolderPath.Trim(),
            OutputFolderPath = this.OutputFolderPath.Trim(),
            Width = width,
            Height = height,
            Zoom = zoom,
            PinImagePath = this.PinImagePath.Trim(),
            TileSourceKey = this.SelectedTileSource.Key,
            CustomTileUrlTemplate = this.CustomTileUrlTemplate.Trim(),
            CustomTileAttribution = this.CustomTileAttribution.Trim(),
        });

        this.StatusMessage = "設定を保存しました。";
        return true;
    }

    private void SaveSettings()
    {
        _ = this.TrySaveSettings();
    }

    private void ClearFeedback()
    {
        this.ValidationMessage = string.Empty;
        this.StatusMessage = string.Empty;
    }

    private static bool TryGetPositiveInteger(double value, out int result)
    {
        if (TryGetInteger(value, out result) && result > 0)
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryGetInteger(double value, out int result)
    {
        if (double.IsFinite(value)
            && value >= int.MinValue
            && value <= int.MaxValue
            && value == Math.Truncate(value))
        {
            result = (int)value;
            return true;
        }

        result = 0;
        return false;
    }
}
