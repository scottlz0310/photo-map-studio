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
    private readonly object synchronization = new();

    private PreviewGenerationSettings settings = new();
    private IReadOnlyList<PreviewPhoto> photos = [];
    private PreviewPhoto? selectedPhoto;
    private ReadOnlyMemory<byte> previewImageBytes;
    private GeoCoordinate? previewCoordinate;
    private string statusMessage = string.Empty;
    private string attribution = "出典未設定";
    private Uri? attributionUri;
    private bool isGenerating;
    private bool hasError;
    private PendingPreviewRequest? pendingRequest;
    private CancellationTokenSource? activeCancellation;
    private TaskCompletionSource? idleCompletion;
    private long requestVersion;
    private bool isProcessing;
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

    /// <summary>現在選択中の写真。</summary>
    public PreviewPhoto? SelectedPhoto
    {
        get => this.selectedPhoto;
        set
        {
            if (this.SetProperty(ref this.selectedPhoto, value))
            {
                this.QueueRefresh(this.settings, reloadPhotos: false);
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

        bool inputFolderChanged = !string.Equals(
            this.settings.InputFolderPath,
            settings.InputFolderPath,
            StringComparison.OrdinalIgnoreCase);

        this.settings = settings;
        this.UpdateAttribution(settings);

        bool shouldReloadPhotos = reloadPhotos
            || inputFolderChanged
            || (this.Photos.Count == 0 && !string.IsNullOrWhiteSpace(settings.InputFolderPath));
        this.QueueRefresh(settings, shouldReloadPhotos);
    }

    /// <summary>
    /// 現在の生成処理が落ち着くまで待つ。テストと終了処理で使用する。
    /// </summary>
    public Task WaitForIdleAsync()
    {
        lock (this.synchronization)
        {
            return this.isProcessing
                ? this.idleCompletion!.Task
                : Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (this.synchronization)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.pendingRequest = null;
            this.requestVersion++;
            CancellationTokenSource? cancellation = this.activeCancellation;
            this.activeCancellation = null;
            cancellation?.Cancel();
            cancellation?.Dispose();
            this.idleCompletion?.TrySetResult();
        }
    }

    private void UpdateAttribution(PreviewGenerationSettings settings)
    {
        PhotoMapStudio.Core.Tiles.TileSource? tileSource = settings.SelectedTileSource.IsCustom
            ? null
            : settings.SelectedTileSource.Source;
        string attribution = settings.SelectedTileSource.IsCustom
            ? settings.CustomTileAttribution.Trim()
            : tileSource?.Attribution ?? string.Empty;

        this.Attribution = string.IsNullOrWhiteSpace(attribution)
            ? "出典未設定"
            : attribution;
        this.AttributionUri = tileSource?.AttributionUri;
    }

    private void QueueRefresh(PreviewGenerationSettings settings, bool reloadPhotos)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        this.IsGenerating = true;
        this.HasError = false;
        this.PreviewImageBytes = ReadOnlyMemory<byte>.Empty;
        this.PreviewCoordinate = null;
        this.StatusMessage = "プレビューを更新しています...";

        bool shouldStartProcessing;
        lock (this.synchronization)
        {
            long version = ++this.requestVersion;
            this.pendingRequest = new PendingPreviewRequest(settings, reloadPhotos, version);
            this.activeCancellation?.Cancel();

            shouldStartProcessing = !this.isProcessing;
            if (shouldStartProcessing)
            {
                this.isProcessing = true;
                this.idleCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        if (shouldStartProcessing)
        {
            _ = this.ProcessPendingAsync();
        }
    }

    private async Task ProcessPendingAsync()
    {
        while (true)
        {
            PendingPreviewRequest? request = null;
            CancellationTokenSource? cancellation = null;
            TaskCompletionSource? completion = null;

            lock (this.synchronization)
            {
                if (this.pendingRequest is null)
                {
                    this.isProcessing = false;
                    completion = this.idleCompletion;
                    this.idleCompletion = null;
                }
                else
                {
                    request = this.pendingRequest;
                    this.pendingRequest = null;
                    cancellation = new CancellationTokenSource();
                    this.activeCancellation = cancellation;
                }
            }

            if (completion is not null)
            {
                completion.TrySetResult();
                return;
            }

            try
            {
                await this.ProcessRequestAsync(request!, cancellation!.Token)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellation!.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is ExifGpsReadException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
            {
                if (this.IsCurrent(request!.Version))
                {
                    this.PreviewImageBytes = ReadOnlyMemory<byte>.Empty;
                    this.PreviewCoordinate = null;
                    this.HasError = true;
                    this.StatusMessage = $"プレビュー生成エラー: {exception.Message}";
                }
            }
            finally
            {
                lock (this.synchronization)
                {
                    if (ReferenceEquals(this.activeCancellation, cancellation))
                    {
                        this.activeCancellation = null;
                    }
                }

                cancellation!.Dispose();
                if (this.IsCurrent(request!.Version))
                {
                    this.IsGenerating = false;
                }
            }
        }
    }

    private async Task ProcessRequestAsync(
        PendingPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ReloadPhotos)
        {
            this.StatusMessage = "GPS 情報を持つ写真を検索しています...";
            IReadOnlyList<PreviewPhoto> loadedPhotos = await this.previewGenerationService
                .LoadPhotosAsync(request.Settings.InputFolderPath, cancellationToken)
                .ConfigureAwait(true);

            if (!this.IsCurrent(request.Version))
            {
                return;
            }

            string? selectedPath = this.SelectedPhoto?.FilePath;
            this.Photos = loadedPhotos;
            this.SetSelectedPhotoWithoutRefresh(
                loadedPhotos.FirstOrDefault(photo => string.Equals(
                    photo.FilePath,
                    selectedPath,
                    StringComparison.OrdinalIgnoreCase))
                ?? (loadedPhotos.Count > 0 ? loadedPhotos[0] : null));
        }

        if (!this.IsCurrent(request.Version))
        {
            return;
        }

        PreviewGenerationResult result = await this.previewGenerationService.GenerateAsync(
            this.SelectedPhoto,
            request.Settings,
            cancellationToken).ConfigureAwait(true);

        if (!this.IsCurrent(request.Version))
        {
            return;
        }

        this.PreviewImageBytes = result.Succeeded && !result.Image.IsEmpty
            ? result.Image
            : ReadOnlyMemory<byte>.Empty;
        this.PreviewCoordinate = result.Coordinate;
        this.HasError = !result.Succeeded;
        this.StatusMessage = result.Message;
    }

    private void SetSelectedPhotoWithoutRefresh(PreviewPhoto? photo)
        => this.SetProperty(ref this.selectedPhoto, photo, nameof(this.SelectedPhoto));

    private bool IsCurrent(long version)
    {
        lock (this.synchronization)
        {
            return !this.disposed && version == this.requestVersion;
        }
    }

    private void Cancel()
    {
        bool hadWork;
        lock (this.synchronization)
        {
            hadWork = this.isProcessing || this.pendingRequest is not null;
            if (!hadWork)
            {
                return;
            }

            this.pendingRequest = null;
            this.requestVersion++;
            this.activeCancellation?.Cancel();
        }

        this.IsGenerating = false;
        this.HasError = false;
        this.StatusMessage = "プレビュー生成をキャンセルしました。";
    }

    private sealed record PendingPreviewRequest(
        PreviewGenerationSettings Settings,
        bool ReloadPhotos,
        long Version);
}
