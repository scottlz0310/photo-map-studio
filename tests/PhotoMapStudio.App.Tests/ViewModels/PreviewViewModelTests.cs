using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;
using PhotoMapStudio.Core.Geo;

namespace PhotoMapStudio.App.Tests.ViewModels;

public class PreviewViewModelTests
{
    [Fact]
    public async Task 設定変更時に旧世代をキャンセルして最新結果だけを反映する()
    {
        var service = new ControlledPreviewGenerationService(
            [new PreviewPhoto("photo.jpg")]);
        using var viewModel = new PreviewViewModel(service);

        viewModel.UpdateSettings(
            new PreviewGenerationSettings { InputFolderPath = "C:\\Photos" },
            reloadPhotos: true);
        await service.FirstGenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.UpdateSettings(
            new PreviewGenerationSettings
            {
                InputFolderPath = "C:\\Photos",
                Width = 1024,
            });

        await service.FirstGenerationCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.WaitForIdleAsync();

        Assert.Equal([2], viewModel.PreviewImageBytes.ToArray());
        Assert.Equal("最新のプレビュー", viewModel.StatusMessage);
        Assert.False(viewModel.IsGenerating);
    }

    [Fact]
    public async Task キャンセル後は生成結果を反映しない()
    {
        var service = new ControlledPreviewGenerationService(
            [new PreviewPhoto("photo.jpg")]);
        using var viewModel = new PreviewViewModel(service);

        viewModel.UpdateSettings(
            new PreviewGenerationSettings { InputFolderPath = "C:\\Photos" },
            reloadPhotos: true);
        await service.FirstGenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CancelCommand.Execute(null);
        await viewModel.WaitForIdleAsync();

        Assert.False(viewModel.IsGenerating);
        Assert.True(viewModel.PreviewImageBytes.IsEmpty);
        Assert.Equal("プレビュー生成をキャンセルしました。", viewModel.StatusMessage);
    }

    private sealed class ControlledPreviewGenerationService(
        IReadOnlyList<PreviewPhoto> photos) : IPreviewGenerationService
    {
        private int generationCount;

        public TaskCompletionSource FirstGenerationStarted { get; } = NewCompletionSource();

        public TaskCompletionSource FirstGenerationCanceled { get; } = NewCompletionSource();

        public Task<IReadOnlyList<PreviewPhoto>> LoadPhotosAsync(
            string folderPath,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PreviewPhoto>>(photos);

        public async Task<PreviewGenerationResult> GenerateAsync(
            PreviewPhoto? photo,
            PreviewGenerationSettings settings,
            CancellationToken cancellationToken)
        {
            int count = Interlocked.Increment(ref this.generationCount);
            if (count == 1)
            {
                this.FirstGenerationStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    this.FirstGenerationCanceled.TrySetResult();
                    throw;
                }
            }

            return new PreviewGenerationResult(
                new byte[] { 2 },
                new GeoCoordinate(35.68123, 139.76712),
                "最新のプレビュー",
                Succeeded: true);
        }

        private static TaskCompletionSource NewCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
