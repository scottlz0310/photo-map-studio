using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;

namespace PhotoMapStudio.App.Tests.ViewModels;

public class MainViewModelGenerationTests
{
    [Fact]
    public async Task 一括生成の進捗と集計を表示する()
    {
        using var input = new TemporaryDirectory();
        using var output = new TemporaryDirectory(createDirectory: false);
        var service = new RecordingBatchGenerationService();
        var viewModel = new MainViewModel(
            new InMemorySettingsRepository(),
            batchGenerationService: service)
        {
            InputFolderPath = input.Path,
            OutputFolderPath = output.Path,
        };

        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsGenerating);
        Assert.Single(viewModel.GenerationLogs);
        Assert.Equal(100, viewModel.GenerationProgressValue);
        Assert.Contains("完了: 処理済み 1 / 全 1 枚", viewModel.GenerationSummary, StringComparison.Ordinal);
        Assert.Equal("SUCCESS", viewModel.GenerationLogs[0].StatusText);

        viewModel.IncludeSubfolders = true;
        Assert.Empty(viewModel.GenerationSummary);
        Assert.Empty(viewModel.ProgressMessage);
        Assert.Equal(0, viewModel.ProgressValue);
    }

    [Fact]
    public async Task 一括生成のキャンセル状態を表示する()
    {
        using var input = new TemporaryDirectory();
        using var output = new TemporaryDirectory(createDirectory: false);
        var service = new BlockingBatchGenerationService();
        var viewModel = new MainViewModel(
            new InMemorySettingsRepository(),
            batchGenerationService: service)
        {
            InputFolderPath = input.Path,
            OutputFolderPath = output.Path,
        };

        Task execution = viewModel.GenerateCommand.ExecuteAsync(null);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsGenerating);
        viewModel.CancelGenerationCommand.Execute(null);
        await execution.ConfigureAwait(true);

        Assert.False(viewModel.IsGenerating);
        Assert.True(service.CancellationObserved);
        Assert.Contains("キャンセル", viewModel.GenerationProgressMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 一括生成の完了後にタイルキャッシュ使用量の表示を実体へ合わせる()
    {
        using var input = new TemporaryDirectory();
        using var output = new TemporaryDirectory(createDirectory: false);
        var cache = new GrowingTileCache { Size = 1024L * 1024L };
        var service = new CacheGrowingBatchGenerationService(cache, 5L * 1024L * 1024L);
        var viewModel = new MainViewModel(
            new InMemorySettingsRepository(),
            batchGenerationService: service,
            tileCache: cache)
        {
            InputFolderPath = input.Path,
            OutputFolderPath = output.Path,
        };
        Assert.Contains("1.0 MB /", viewModel.TileCacheUsageText, StringComparison.Ordinal);

        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Contains("6.0 MB /", viewModel.TileCacheUsageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task プレビュー生成の完了後にタイルキャッシュ使用量の表示を実体へ合わせる()
    {
        using var input = new TemporaryDirectory();
        var cache = new GrowingTileCache { Size = 1024L * 1024L };
        using var preview = new PreviewViewModel(new CacheGrowingPreviewGenerationService(cache, 3L * 1024L * 1024L));
        var viewModel = new MainViewModel(new InMemorySettingsRepository(), preview, tileCache: cache);
        await preview.WaitForIdleAsync();

        // 画面の使用量が、ここまでのプレビュー生成で増えた実体と一致する
        double expectedMb = cache.Size / (1024.0 * 1024.0);
        Assert.True(expectedMb > 1.0);
        Assert.Contains($"使用量: {expectedMb:F1} MB /", viewModel.TileCacheUsageText, StringComparison.Ordinal);

        long sizeBefore = cache.Size;
        viewModel.InputFolderPath = input.Path;
        await preview.WaitForIdleAsync();

        Assert.True(cache.Size > sizeBefore);
        Assert.Contains($"使用量: {cache.Size / (1024.0 * 1024.0):F1} MB /", viewModel.TileCacheUsageText, StringComparison.Ordinal);
    }

    private sealed class GrowingTileCache : PhotoMapStudio.Core.Tiles.ITileCache
    {
        public long Size { get; set; }

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken) => Task.CompletedTask;

        public long GetCurrentSizeBytes() => this.Size;
    }

    private sealed class CacheGrowingBatchGenerationService(GrowingTileCache cache, long growth) : IBatchGenerationService
    {
        public Task<BatchGenerationSummary> GenerateAsync(
            BatchGenerationSettings settings,
            IProgress<BatchGenerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            cache.Size += growth;
            return Task.FromResult(new BatchGenerationSummary(1, 0, 1, IsCancelled: false));
        }
    }

    private sealed class CacheGrowingPreviewGenerationService(GrowingTileCache cache, long growth) : IPreviewGenerationService
    {
        public async IAsyncEnumerable<PreviewPhoto> LoadPhotosAsync(
            string folderPath,
            bool includeSubfolders,
            IProgress<PreviewLoadProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new PreviewPhoto("photo.jpg");
        }

        public Task<PreviewGenerationResult> GenerateAsync(
            PreviewPhoto? photo,
            PreviewGenerationSettings settings,
            CancellationToken cancellationToken)
        {
            cache.Size += growth;
            return Task.FromResult(new PreviewGenerationResult(
                new byte[] { 1 },
                new PhotoMapStudio.Core.Geo.GeoCoordinate(35.0, 139.0),
                "プレビュー",
                Succeeded: true));
        }
    }

    private sealed class InMemorySettingsRepository : IPhotoMapSettingsRepository
    {
        public PhotoMapSettings Load() => new();

        public void Save(PhotoMapSettings settings)
        {
        }
    }

    private sealed class RecordingBatchGenerationService : IBatchGenerationService
    {
        public Task<BatchGenerationSummary> GenerateAsync(
            BatchGenerationSettings settings,
            IProgress<BatchGenerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new BatchGenerationProgress(
                1,
                1,
                "photo.jpg",
                BatchGenerationStatus.Success,
                "生成しました。"));
            return Task.FromResult(new BatchGenerationSummary(1, 0, 1, IsCancelled: false));
        }
    }

    private sealed class BlockingBatchGenerationService : IBatchGenerationService
    {
        public TaskCompletionSource Started { get; } = NewCompletionSource();

        public bool CancellationObserved { get; private set; }

        public async Task<BatchGenerationSummary> GenerateAsync(
            BatchGenerationSettings settings,
            IProgress<BatchGenerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            this.Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this.CancellationObserved = true;
                progress?.Report(new BatchGenerationProgress(
                    1,
                    1,
                    "photo.jpg",
                    BatchGenerationStatus.Cancelled,
                    "処理が手動でキャンセルされました。"));
                return new BatchGenerationSummary(0, 0, 1, IsCancelled: true);
            }

            throw new InvalidOperationException("キャンセルされませんでした。");
        }

        private static TaskCompletionSource NewCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(bool createDirectory = true)
        {
            this.Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"photo-map-vm-{Guid.NewGuid():N}");
            if (createDirectory)
            {
                Directory.CreateDirectory(this.Path);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(this.Path))
            {
                Directory.Delete(this.Path, recursive: true);
            }
        }
    }
}
