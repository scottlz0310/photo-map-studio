using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Photos;

namespace PhotoMapStudio.App.ViewModels;

/// <summary>
/// プレビュー対象の列挙・切り替え・生成状態を管理する。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML の DataContext と App.Tests の状態契約として公開する。")]
public sealed class PreviewViewModel : ObservableObject, IDisposable
{
    private readonly IPreviewGenerationService previewGenerationService;

    private PreviewGenerationSettings settings = new();
    private IReadOnlyList<PreviewPhoto> photos = [];
    private PreviewPhoto? selectedPhoto;
    private ReadOnlyMemory<byte> previewImageBytes;
    private GeoCoordinate? previewCoordinate;
    private string statusMessage = string.Empty;
    private string attribution = "出典未設定";
    private Uri? attributionUri;
    private bool isGenerating;
    private PreviewLoadProgress? loadProgress;
    private bool hasError;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "LoadAsyncのusingで破棄する。終了前のDisposeはToken参照と競合する。")]
    private CancellationTokenSource? loadCancellation;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "RenderAsyncのusingで破棄する。終了前のDisposeはToken参照と競合する。")]
    private CancellationTokenSource? renderCancellation;
    private Task loadingTask = Task.CompletedTask;
    private Task renderingTask = Task.CompletedTask;
    private long loadVersion;
    private long renderVersion;
    private bool isLoading;
    private bool isRendering;
    private bool disposed;

    /// <summary>
    /// ViewModel を構築する。
    /// </summary>
    /// <param name="previewGenerationService">プレビュー生成サービス。</param>
    public PreviewViewModel(IPreviewGenerationService previewGenerationService)
    {
        this.previewGenerationService = previewGenerationService
            ?? throw new ArgumentNullException(nameof(previewGenerationService));
        this.CancelCommand = new RelayCommand(this.Cancel);
    }

    /// <summary>GPS 情報を持つプレビュー対象。</summary>
    public IReadOnlyList<PreviewPhoto> Photos
    {
        get => this.photos;
        private set => this.SetProperty(ref this.photos, value);
    }

    /// <summary>写真一覧の読み込み進捗。</summary>
    public PreviewLoadProgress? LoadProgress
    {
        get => this.loadProgress;
        private set => this.SetProperty(ref this.loadProgress, value);
    }

    /// <summary>現在選択中の写真。</summary>
    public PreviewPhoto? SelectedPhoto
    {
        get => this.selectedPhoto;
        set
        {
            if (this.SetProperty(ref this.selectedPhoto, value))
            {
                this.BeginRender();
            }
        }
    }

    /// <summary>生成された PNG のバイト列。</summary>
    public ReadOnlyMemory<byte> PreviewImageBytes
    {
        get => this.previewImageBytes;
        private set
        {
            if (this.SetProperty(ref this.previewImageBytes, value))
            {
                this.OnPropertyChanged(nameof(this.HasPreviewImage));
                this.OnPropertyChanged(nameof(this.IsEmptyStateVisible));
            }
        }
    }

    /// <summary>プレビューに使用した GPS 座標。</summary>
    public GeoCoordinate? PreviewCoordinate
    {
        get => this.previewCoordinate;
        private set => this.SetProperty(ref this.previewCoordinate, value);
    }

    /// <summary>プレビューの状態メッセージ。</summary>
    public string StatusMessage
    {
        get => this.statusMessage;
        private set => this.SetProperty(ref this.statusMessage, value);
    }

    /// <summary>プレビューに表示する出典。</summary>
    public string Attribution
    {
        get => this.attribution;
        private set => this.SetProperty(ref this.attribution, value);
    }

    /// <summary>出典・ライセンスページへのリンク。</summary>
    public Uri? AttributionUri
    {
        get => this.attributionUri;
        private set
        {
            if (this.SetProperty(ref this.attributionUri, value))
            {
                this.OnPropertyChanged(nameof(this.HasAttributionUri));
            }
        }
    }

    /// <summary>出典リンクを表示できるかどうか。</summary>
    public bool HasAttributionUri => this.AttributionUri is not null;

    /// <summary>画像が表示されているかどうか。</summary>
    public bool HasPreviewImage => !this.PreviewImageBytes.IsEmpty;

    /// <summary>画像がない状態の案内を表示するかどうか。</summary>
    public bool IsEmptyStateVisible => !this.HasPreviewImage;

    /// <summary>生成処理中かどうか。</summary>
    public bool IsGenerating
    {
        get => this.isGenerating;
        private set => this.SetProperty(ref this.isGenerating, value);
    }

    /// <summary>直近の生成または読み込みがエラーになったかどうか。</summary>
    public bool HasError
    {
        get => this.hasError;
        private set => this.SetProperty(ref this.hasError, value);
    }

    /// <summary>実行中のプレビュー生成をキャンセルする。</summary>
    public ICommand CancelCommand { get; }

    /// <summary>
    /// 設定変更を受け取り、必要なら写真一覧とプレビューを更新する。
    /// </summary>
    /// <param name="settings">未保存の設定スナップショット。</param>
    /// <param name="reloadPhotos">写真一覧を再列挙するかどうか。</param>
    public void UpdateSettings(PreviewGenerationSettings settings, bool reloadPhotos = false)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        bool inputChanged = !string.Equals(this.settings.InputFolderPath, settings.InputFolderPath, StringComparison.OrdinalIgnoreCase)
            || this.settings.IncludeSubfolders != settings.IncludeSubfolders;
        this.settings = settings;
        this.UpdateAttribution(settings);
        if (reloadPhotos || inputChanged || (this.Photos.Count == 0 && !this.isLoading && !string.IsNullOrWhiteSpace(settings.InputFolderPath)))
        {
            this.BeginLoad();
        }
        else if (!this.isLoading || this.SelectedPhoto is not null)
        {
            this.BeginRender();
        }
    }

    /// <summary>一覧読み込みと最新の生成が終了するまで待つ。</summary>
    public async Task WaitForIdleAsync()
    {
        while (true)
        {
            Task loading = this.loadingTask;
            Task rendering = this.renderingTask;
            await Task.WhenAll(loading, rendering).ConfigureAwait(true);
            if (ReferenceEquals(loading, this.loadingTask) && ReferenceEquals(rendering, this.renderingTask)) { return; }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed) { return; }
        this.disposed = true;
        this.loadVersion++; this.renderVersion++;
        this.loadCancellation?.Cancel();
        this.renderCancellation?.Cancel();
    }

    private void UpdateAttribution(PreviewGenerationSettings current)
    {
        PhotoMapStudio.Core.Tiles.TileSource? source = current.SelectedTileSource.IsCustom ? null : current.SelectedTileSource.Source;
        string value = current.SelectedTileSource.IsCustom ? current.CustomTileAttribution.Trim() : source?.Attribution ?? string.Empty;
        this.Attribution = string.IsNullOrWhiteSpace(value) ? "出典未設定" : value;
        this.AttributionUri = source?.AttributionUri;
    }

    private void BeginLoad()
    {
        this.loadCancellation?.Cancel();
        this.renderCancellation?.Cancel();
        this.renderVersion++;
        this.Photos = new System.Collections.ObjectModel.ObservableCollection<PreviewPhoto>();
        this.SetProperty(ref this.selectedPhoto, null, nameof(this.SelectedPhoto));
        this.PreviewImageBytes = ReadOnlyMemory<byte>.Empty;
        this.PreviewCoordinate = null;
        this.LoadProgress = null;
        this.HasError = false;
        this.StatusMessage = "GPS情報を持つ写真を検索しています...";
        this.isLoading = true;
        this.IsGenerating = true;
        var cancellation = new CancellationTokenSource();
        this.loadCancellation = cancellation;
        this.loadingTask = this.LoadAsync(this.settings, ++this.loadVersion, cancellation, this.loadingTask);
    }

    private async Task LoadAsync(PreviewGenerationSettings current, long version, CancellationTokenSource cancellation, Task previous)
    {
        using (cancellation)
        {
            try
            {
                await previous.ConfigureAwait(true);
                cancellation.Token.ThrowIfCancellationRequested();
                var progress = new Progress<PreviewLoadProgress>(value =>
                {
                    if (!this.disposed && version == this.loadVersion && !cancellation.IsCancellationRequested)
                    {
                        this.LoadProgress = value;
                        this.HasError |= value.IsError;
                    }
                });
                await foreach (PreviewPhoto photo in this.previewGenerationService.LoadPhotosAsync(current.InputFolderPath,
                    current.IncludeSubfolders, progress, cancellation.Token).ConfigureAwait(true))
                {
                    if (this.disposed || version != this.loadVersion || cancellation.IsCancellationRequested) { return; }
                    ((System.Collections.ObjectModel.ObservableCollection<PreviewPhoto>)this.Photos).Add(photo);
                    if (this.SelectedPhoto is null)
                    {
                        this.SetProperty(ref this.selectedPhoto, photo, nameof(this.SelectedPhoto));
                        this.BeginRender();
                    }
                }

                if (this.Photos.Count == 0 && version == this.loadVersion) { this.StatusMessage = "GPS情報を持つ写真が見つかりませんでした。"; }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception) when (exception is ExifGpsReadException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                if (version == this.loadVersion && !this.disposed)
                {
                    this.HasError = true;
                    this.StatusMessage = $"写真一覧読み込みエラー: {exception.Message}";
                }
            }
            finally
            {
                if (ReferenceEquals(this.loadCancellation, cancellation))
                {
                    this.loadCancellation = null;
                    this.isLoading = false;
                    this.UpdateBusy();
                }
            }
        }
    }

    private void BeginRender()
    {
        if (this.disposed) { return; }
        this.renderCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        this.renderCancellation = cancellation;
        this.isRendering = true;
        this.IsGenerating = true;
        this.HasError = false;
        this.PreviewImageBytes = ReadOnlyMemory<byte>.Empty;
        this.PreviewCoordinate = null;
        this.StatusMessage = "プレビューを更新しています...";
        this.renderingTask = this.RenderAsync(this.SelectedPhoto, this.settings, ++this.renderVersion, cancellation, this.renderingTask);
    }

    private async Task RenderAsync(PreviewPhoto? photo, PreviewGenerationSettings current, long version, CancellationTokenSource cancellation, Task previous)
    {
        using (cancellation)
        {
            try
            {
                await previous.ConfigureAwait(true);
                cancellation.Token.ThrowIfCancellationRequested();
                PreviewGenerationResult result = await this.previewGenerationService.GenerateAsync(photo, current, cancellation.Token).ConfigureAwait(true);
                if (this.disposed || version != this.renderVersion || cancellation.IsCancellationRequested) { return; }
                this.PreviewImageBytes = result.Succeeded ? result.Image : ReadOnlyMemory<byte>.Empty;
                this.PreviewCoordinate = result.Coordinate;
                this.HasError = !result.Succeeded;
                this.StatusMessage = result.Message;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception) when (exception is ExifGpsReadException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                if (version == this.renderVersion && !this.disposed) { this.HasError = true; this.StatusMessage = $"プレビュー生成エラー: {exception.Message}"; }
            }
            finally
            {
                if (ReferenceEquals(this.renderCancellation, cancellation))
                {
                    this.renderCancellation = null;
                    this.isRendering = false;
                    this.UpdateBusy();
                }
            }
        }
    }

    private void UpdateBusy()
    {
        this.IsGenerating = this.isLoading || this.isRendering;
        if (!this.IsGenerating && this.StatusMessage == "キャンセルしています...")
        {
            this.StatusMessage = "プレビュー生成をキャンセルしました。";
        }
    }

    private void Cancel()
    {
        if (!this.IsGenerating) { return; }
        this.loadVersion++; this.renderVersion++;
        this.StatusMessage = "キャンセルしています...";
        this.loadCancellation?.Cancel();
        this.renderCancellation?.Cancel();
    }
}
