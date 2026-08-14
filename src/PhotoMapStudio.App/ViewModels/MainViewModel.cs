using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    private readonly IBatchGenerationService? batchGenerationService;
    private CancellationTokenSource? generationCancellation;
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
    private bool isGenerating;
    private double generationProgressValue;
    private string generationProgressMessage = string.Empty;
    private string generationSummary = string.Empty;
    private bool hasGenerationError;

    /// <summary>
    /// ViewModel を構築する。
    /// </summary>
    /// <param name="settingsRepository">設定リポジトリ。</param>
    public MainViewModel(
        IPhotoMapSettingsRepository settingsRepository,
        PreviewViewModel? preview = null,
        IBatchGenerationService? batchGenerationService = null)
    {
        this.settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        this.batchGenerationService = batchGenerationService;
        this.Preview = preview;

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
        this.GenerateCommand = new AsyncRelayCommand(this.GenerateAsync, this.CanGenerate);
        this.CancelGenerationCommand = new RelayCommand(this.CancelGeneration, this.CanCancelGeneration);
        this.Preview?.UpdateSettings(this.CreatePreviewSettings());
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
                this.NotifyPreviewChanged(reloadPhotos: true);
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
                this.NotifyPreviewChanged();
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
                this.NotifyPreviewChanged();
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
                this.NotifyPreviewChanged();
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
                this.NotifyPreviewChanged();
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
            this.NotifyPreviewChanged();
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
                this.NotifyPreviewChanged();
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
                this.NotifyPreviewChanged();
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

    /// <summary>プレビューの状態。</summary>
    public PreviewViewModel? Preview { get; }

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

    /// <summary>一括生成コマンド。</summary>
    public IAsyncRelayCommand GenerateCommand { get; }

    /// <summary>一括生成キャンセルコマンド。</summary>
    public IRelayCommand CancelGenerationCommand { get; }

    /// <summary>一括生成の進捗ログ。</summary>
    public ObservableCollection<BatchGenerationProgress> GenerationLogs { get; } = new();

    /// <summary>一括生成中かどうか。</summary>
    public bool IsGenerating
    {
        get => this.isGenerating;
        private set
        {
            if (this.SetProperty(ref this.isGenerating, value))
            {
                this.GenerateCommand.NotifyCanExecuteChanged();
                this.CancelGenerationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>進捗バーの値（0〜100）。</summary>
    public double GenerationProgressValue
    {
        get => this.generationProgressValue;
        private set
        {
            if (this.SetProperty(ref this.generationProgressValue, value))
            {
                this.OnPropertyChanged(nameof(this.GenerationProgressPercentText));
            }
        }
    }

    /// <summary>進捗バーの表示用パーセント。</summary>
    public string GenerationProgressPercentText => $"{this.GenerationProgressValue:0}%";

    /// <summary>現在の進捗メッセージ。</summary>
    public string GenerationProgressMessage
    {
        get => this.generationProgressMessage;
        private set => this.SetProperty(ref this.generationProgressMessage, value);
    }

    /// <summary>一括生成の集計メッセージ。</summary>
    public string GenerationSummary
    {
        get => this.generationSummary;
        private set => this.SetProperty(ref this.generationSummary, value);
    }

    /// <summary>一括生成中にエラーが発生したかどうか。</summary>
    public bool HasGenerationError
    {
        get => this.hasGenerationError;
        private set => this.SetProperty(ref this.hasGenerationError, value);
    }

    /// <summary>
    /// 起動引数から入力・出力フォルダを適用する。
    /// </summary>
    /// <param name="arguments">解析済み起動引数。</param>
    internal void ApplyLaunchArguments(LaunchArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.InputDirectoryPath is not null)
        {
            this.InputFolderPath = arguments.InputDirectoryPath;
        }

        if (arguments.OutputDirectoryPath is not null)
        {
            this.OutputFolderPath = arguments.OutputDirectoryPath;
        }

        if (arguments.Errors.Count > 0)
        {
            this.ValidationMessage = string.Join(Environment.NewLine, arguments.Errors);
        }
    }

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

    private bool CanGenerate()
        => this.batchGenerationService is not null && !this.IsGenerating;

    private bool CanCancelGeneration()
        => this.IsGenerating;

    private async Task GenerateAsync()
    {
        if (this.batchGenerationService is null || !this.TryCreateBatchGenerationSettings(out BatchGenerationSettings settings))
        {
            return;
        }

        this.GenerationLogs.Clear();
        this.GenerationProgressValue = 0;
        this.GenerationProgressMessage = "一括生成を開始しています...";
        this.GenerationSummary = string.Empty;
        this.HasGenerationError = false;
        this.IsGenerating = true;

        using var cancellation = new CancellationTokenSource();
        this.generationCancellation = cancellation;

        try
        {
            var progress = new Progress<BatchGenerationProgress>(this.ReportProgress);
            BatchGenerationSummary summary = await this.batchGenerationService
                .GenerateAsync(settings, progress, cancellation.Token)
                .ConfigureAwait(true);

            this.GenerationProgressValue = summary.TotalCount == 0 ? 100 : this.GenerationProgressValue;
            this.GenerationSummary = FormatSummary(summary);
            this.GenerationProgressMessage = summary.IsCancelled
                ? "処理がキャンセルされました。"
                : "一括生成が完了しました。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            this.GenerationProgressMessage = "処理がキャンセルされました。";
        }
        catch (Exception exception) when (exception is BatchGenerationException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException)
        {
            this.HasGenerationError = true;
            this.ValidationMessage = exception.Message;
            this.GenerationProgressMessage = "一括生成を開始できませんでした。";
        }
        finally
        {
            if (ReferenceEquals(this.generationCancellation, cancellation))
            {
                this.generationCancellation = null;
            }

            this.IsGenerating = false;
        }
    }

    private void CancelGeneration()
    {
        if (this.generationCancellation is null)
        {
            return;
        }

        this.GenerationProgressMessage = "キャンセルしています...";
        this.generationCancellation.Cancel();
    }

    private void ReportProgress(BatchGenerationProgress progress)
    {
        this.GenerationLogs.Add(progress);
        this.GenerationProgressValue = progress.Total == 0
            ? 100
            : progress.Index * 100d / progress.Total;
        this.GenerationProgressMessage = progress.Message;
        this.HasGenerationError |= progress.Status == BatchGenerationStatus.Error;
    }

    private bool TryCreateBatchGenerationSettings(out BatchGenerationSettings settings)
    {
        settings = null!;
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

        string inputFolderPath = this.InputFolderPath.Trim();
        if (string.IsNullOrWhiteSpace(inputFolderPath))
        {
            this.ValidationMessage = "入力フォルダを指定してください。";
            return false;
        }

        if (!Directory.Exists(inputFolderPath))
        {
            this.ValidationMessage = "指定された入力フォルダが存在しません。";
            return false;
        }

        string outputFolderPath = this.OutputFolderPath.Trim();
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            this.ValidationMessage = "出力フォルダを指定してください。";
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

        settings = new BatchGenerationSettings
        {
            InputFolderPath = inputFolderPath,
            OutputFolderPath = outputFolderPath,
            Width = width,
            Height = height,
            Zoom = zoom,
            PinImagePath = this.PinImagePath.Trim(),
            TileSource = tileSource,
        };
        return true;
    }

    private static string FormatSummary(BatchGenerationSummary summary)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{(summary.IsCancelled ? "キャンセル" : "完了")}: 成功 {summary.SuccessCount} / スキップ {summary.SkippedCount} / 総数 {summary.TotalCount}");

    private void ClearFeedback()
    {
        this.ValidationMessage = string.Empty;
        this.StatusMessage = string.Empty;
    }

    private void NotifyPreviewChanged(bool reloadPhotos = false)
        => this.Preview?.UpdateSettings(this.CreatePreviewSettings(), reloadPhotos);

    private PreviewGenerationSettings CreatePreviewSettings()
        => new()
        {
            InputFolderPath = this.InputFolderPath,
            Width = this.Width,
            Height = this.Height,
            Zoom = this.Zoom,
            PinImagePath = this.PinImagePath,
            SelectedTileSource = this.SelectedTileSource,
            CustomTileUrlTemplate = this.CustomTileUrlTemplate,
            CustomTileAttribution = this.CustomTileAttribution,
        };

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
