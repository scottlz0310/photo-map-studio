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
        Assert.Equal("完了: 成功 1 / スキップ 0 / 総数 1", viewModel.GenerationSummary);
        Assert.Equal("SUCCESS", viewModel.GenerationLogs[0].StatusText);
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
