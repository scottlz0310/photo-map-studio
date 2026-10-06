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
    private const string LoopbackTileUsageMessage = "この PC 上の配信元です。一括取得も高速な間隔（8並列・5ミリ秒）で行い、100枚超の確認は省略します。";

    private readonly IPhotoMapSettingsRepository settingsRepository;
    private readonly IBatchGenerationService? batchGenerationService;
    private CancellationTokenSource? generationCancellation;
    private string inputFolderPath;
    private string outputFolderPath;
    private bool includeSubfolders;
    private string outputFilePrefix;
    private string outputFilePostfix;
    private bool isEnumerating;
    private string generationDetails = string.Empty;
    private string generationElapsedText = string.Empty;
    private readonly TimeProvider timeProvider;
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
        IBatchGenerationService? batchGenerationService = null, TimeProvider? timeProvider = null)
    {
        this.settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        this.batchGenerationService = batchGenerationService;
        this.Preview = preview;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        if (preview is not null)
        {
            preview.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PreviewViewModel.LoadProgress))
                {
                    this.OnPropertyChanged(nameof(this.ProgressIsIndeterminate));
                    this.OnPropertyChanged(nameof(this.ProgressMessage));
                    this.OnPropertyChanged(nameof(this.ProgressValue));
                    if (preview.LoadProgress is { IsError: true } failure)
                    {
                        this.GenerationLogs.Add(new(0, failure.TotalCount, string.Empty, BatchGenerationStatus.Error, failure.Message));
                    }
                }
            };
        }

        var settings = this.settingsRepository.Load();
        this.inputFolderPath = settings.InputFolderPath;
        this.outputFolderPath = settings.OutputFolderPath;
        this.includeSubfolders = settings.IncludeSubfolders;
        this.outputFilePrefix = settings.OutputFilePrefix;
        this.outputFilePostfix = settings.OutputFilePostfix;
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

    /// <summary>下位フォルダを探索する。</summary>
    public bool IncludeSubfolders
    {
        get => this.includeSubfolders;
        set { if (this.SetProperty(ref this.includeSubfolders, value)) { this.ClearFeedback(); this.NotifyPreviewChanged(reloadPhotos: true); } }
    }

    /// <summary>出力名の先頭文字。</summary>
    public string OutputFilePrefix
    {
        get => this.outputFilePrefix;
        set { if (this.SetProperty(ref this.outputFilePrefix, value ?? string.Empty)) { this.ClearFeedback(); this.OnPropertyChanged(nameof(this.OutputFileNameExample)); } }
    }

    /// <summary>出力名の末尾文字。</summary>
    public string OutputFilePostfix
    {
        get => this.outputFilePostfix;
        set { if (this.SetProperty(ref this.outputFilePostfix, value ?? string.Empty)) { this.ClearFeedback(); this.OnPropertyChanged(nameof(this.OutputFileNameExample)); } }
    }

    /// <summary>出力名の入力例。</summary>
    public string OutputFileNameExample => $"IMG_0001.jpg → {this.OutputFilePrefix}IMG_0001{this.OutputFilePostfix}.png";

    /// <summary>配信元に応じた一括生成の案内。</summary>
    public string TileUsageMessage => this.SelectedTileSource == TileSourceChoices.OpenStreetMap
        ? "OSM公式サーバーはプレビュー用です。一括生成は、一括取得・画像保存を許可するOSM系配信元をカスタム設定で指定してください。"
        : this.IsCustomTileSource ? this.CustomTileUsageMessage : string.Empty;

    private string CustomTileUsageMessage => TileSource.IsLoopbackUrlTemplate(this.CustomTileUrlTemplate)
        ? LoopbackTileUsageMessage
        : "配信元の一括取得・画像保存の許可を確認してください。一括取得は1秒間隔、100枚超は開始前に確認します。";

    /// <summary>配信元の案内を表示するかどうか。</summary>
    public bool HasTileUsageMessage => !string.IsNullOrEmpty(this.TileUsageMessage);

    /// <summary>画面側で大量実行の許可を確認する。</summary>
    public Func<int, CancellationToken, Task<bool>>? ConfirmLargeBatchAsync { get; set; }

    /// <summary>列挙中は件数が確定しない。</summary>
    public bool ProgressIsIndeterminate => this.IsGenerating ? this.isEnumerating : this.Preview?.LoadProgress?.IsEnumerating == true;

    /// <summary>3フェーズを共通領域で表示する。</summary>
    public string ProgressMessage => this.IsGenerating || !string.IsNullOrEmpty(this.GenerationSummary)
        ? this.GenerationProgressMessage : this.Preview?.LoadProgress?.Message ?? string.Empty;

    /// <summary>確定した対象数に対する進捗。</summary>
    public double ProgressValue => this.IsGenerating || !string.IsNullOrEmpty(this.GenerationSummary)
        ? this.GenerationProgressValue : this.Preview?.LoadProgress is { TotalCount: > 0 } loading ? loading.CheckedCount * 100d / loading.TotalCount : 0;

    /// <summary>一括生成の件数とタイル集計。</summary>
    public string GenerationDetails { get => this.generationDetails; private set => this.SetProperty(ref this.generationDetails, value); }

    /// <summary>一括生成開始からの経過時間。</summary>
    public string GenerationElapsedText { get => this.generationElapsedText; private set => this.SetProperty(ref this.generationElapsedText, value); }

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
            this.OnPropertyChanged(nameof(this.TileUsageMessage));
            this.OnPropertyChanged(nameof(this.HasTileUsageMessage));
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
                this.OnPropertyChanged(nameof(this.TileUsageMessage));
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
                this.OnPropertyChanged(nameof(this.ProgressIsIndeterminate));
                this.OnPropertyChanged(nameof(this.ProgressMessage));
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
                this.OnPropertyChanged(nameof(this.ProgressValue));
            }
        }
    }

    /// <summary>進捗バーの表示用パーセント。</summary>
    public string GenerationProgressPercentText => $"{this.GenerationProgressValue:0}%";

    /// <summary>現在の進捗メッセージ。</summary>
    public string GenerationProgressMessage
    {
        get => this.generationProgressMessage;
        private set { if (this.SetProperty(ref this.generationProgressMessage, value)) { this.OnPropertyChanged(nameof(this.ProgressMessage)); } }
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
            IncludeSubfolders = this.IncludeSubfolders,
            OutputFilePrefix = this.OutputFilePrefix,
            OutputFilePostfix = this.OutputFilePostfix,
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
        using var elapsedCancellation = new CancellationTokenSource();
        Task elapsedTask = this.UpdateElapsedAsync(elapsedCancellation.Token);
        this.generationCancellation = cancellation;

        try
        {
            var progress = new Progress<BatchGenerationProgress>(this.ReportProgress);
            BatchGenerationSummary summary = await this.batchGenerationService
                .GenerateAsync(settings, progress, cancellation.Token)
                .ConfigureAwait(true);

            this.GenerationProgressValue = summary.TotalCount == 0 ? 100 : this.GenerationProgressValue;
            this.GenerationSummary = FormatSummary(summary);
            this.HasGenerationError |= summary.ErrorCount > 0 || summary.StopReason is not null;
            this.GenerationProgressMessage = summary.StopReason ?? (summary.IsCancelled
                ? "処理がキャンセルされました。"
                : "一括生成が完了しました。");
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
            this.GenerationProgressMessage = $"一括生成エラー: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(this.generationCancellation, cancellation))
            {
                this.generationCancellation = null;
            }

            await elapsedCancellation.CancelAsync().ConfigureAwait(true);
            await elapsedTask.ConfigureAwait(true);
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
        this.isEnumerating = progress.IsEnumerating;
        this.OnPropertyChanged(nameof(this.ProgressIsIndeterminate));
        if (!progress.IsActivity) { this.GenerationLogs.Add(progress); }
        this.GenerationDetails = $"処理済み {progress.SuccessCount + progress.SkippedCount + progress.ErrorCount} / 全 {progress.Total} 枚、成功 {progress.SuccessCount} / スキップ {progress.SkippedCount} / エラー {progress.ErrorCount}、現在: {progress.FileName}"
            + FormatTiles(progress.Tiles);
        this.GenerationProgressValue = progress.Total == 0
            ? 0
            : progress.Index * 100d / progress.Total;
        if (this.generationCancellation?.IsCancellationRequested != true) { this.GenerationProgressMessage = progress.Message; }
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

        try
        {
            OutputPathResolver.Validate(outputFolderPath, this.OutputFilePrefix, this.OutputFilePostfix);
            if (this.OutputFilePrefix.Length == 0 && this.OutputFilePostfix.Length == 0)
            {
                _ = OutputPathResolver.Resolve(Path.Combine(inputFolderPath, "IMG_0001.jpg"), new()
                { OutputFolderPath = outputFolderPath, OutputFilePrefix = this.OutputFilePrefix, OutputFilePostfix = this.OutputFilePostfix });
            }
            if (tileSource.IsOfficialOpenStreetMap)
            {
                this.ValidationMessage = "OSM公式サーバーはプレビュー用です。一括取得・画像保存が許可されたOSM系配信元をカスタム設定で指定してください。";
                return false;
            }
        }
        catch (ArgumentException exception)
        {
            this.ValidationMessage = exception.Message;
            return false;
        }

        settings = new BatchGenerationSettings
        {
            InputFolderPath = inputFolderPath,
            OutputFolderPath = outputFolderPath,
            IncludeSubfolders = this.IncludeSubfolders,
            OutputFilePrefix = this.OutputFilePrefix,
            OutputFilePostfix = this.OutputFilePostfix,
            ConfirmLargeBatchAsync = this.ConfirmLargeBatchAsync,
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
            $"{(summary.IsCancelled ? "キャンセル" : summary.StopReason is null ? "完了" : "中止")}: 処理済み {summary.ProcessedCount} / 全 {summary.TotalCount} 枚（成功 {summary.SuccessCount} / スキップ {summary.SkippedCount} / エラー {summary.ErrorCount}）、経過 {summary.Elapsed:c}{FormatTiles(summary.Tiles)}");

    private static string FormatTiles(TileFetchStatistics? tiles) => tiles is null ? string.Empty
        : $" / タイル: ネットワーク取得 {tiles.NetworkCount} 件 / キャッシュ {tiles.CacheCount} 件、平均 {tiles.AverageRate:F2} / 最大 {tiles.MaximumRate} リクエスト/秒";

    private async Task UpdateElapsedAsync(CancellationToken cancellationToken)
    {
        long started = this.timeProvider.GetTimestamp();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                this.GenerationElapsedText = $"経過時間: {this.timeProvider.GetElapsedTime(started):c}";
                await Task.Delay(TimeSpan.FromSeconds(1), this.timeProvider, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void ClearFeedback()
    {
        this.ValidationMessage = string.Empty;
        this.StatusMessage = string.Empty;
    }

    private void NotifyPreviewChanged(bool reloadPhotos = false)
    {
        if (reloadPhotos && !this.IsGenerating)
        {
            this.GenerationSummary = string.Empty;
            this.OnPropertyChanged(nameof(this.ProgressMessage));
            this.OnPropertyChanged(nameof(this.ProgressValue));
        }

        this.Preview?.UpdateSettings(this.CreatePreviewSettings(), reloadPhotos);
    }

    private PreviewGenerationSettings CreatePreviewSettings()
        => new()
        {
            InputFolderPath = this.InputFolderPath,
            IncludeSubfolders = this.IncludeSubfolders,
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
