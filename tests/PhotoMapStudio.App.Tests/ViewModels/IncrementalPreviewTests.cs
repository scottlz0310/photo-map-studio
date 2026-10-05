using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;

namespace PhotoMapStudio.App.Tests.ViewModels;

public class IncrementalPreviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 読み込み完了前に最初の画像を表示しキャンセルしても候補を保持する(bool cancel)
    {
        var service = new StreamingService();
        using var viewModel = new PreviewViewModel(service);
        viewModel.UpdateSettings(new() { InputFolderPath = "C:\\Photos" }, true);
        await service.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, viewModel.Photos.Count);
        Assert.False(viewModel.PreviewImageBytes.IsEmpty);
        if (cancel) { viewModel.CancelCommand.Execute(null); }
        else { service.Continue.TrySetResult(); }
        await viewModel.WaitForIdleAsync();
        Assert.Equal(cancel ? 2 : 3, viewModel.Photos.Count);
        Assert.False(viewModel.PreviewImageBytes.IsEmpty);
        Assert.False(viewModel.IsGenerating);
        viewModel.SelectedPhoto = viewModel.Photos[1];
        await viewModel.WaitForIdleAsync();
        Assert.Equal("b.jpg", viewModel.StatusMessage);
    }

    [Fact]
    public async Task 候補読み込み中に選択しても一覧の読み込みを中断しない()
    {
        var service = new StreamingService();
        using var viewModel = new PreviewViewModel(service);
        viewModel.UpdateSettings(new() { InputFolderPath = "C:\\Photos" }, true);
        await service.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.SelectedPhoto = viewModel.Photos[1];
        Assert.Equal("b.jpg", viewModel.StatusMessage);
        service.Continue.TrySetResult();
        await viewModel.WaitForIdleAsync();
        Assert.Equal(3, viewModel.Photos.Count);
        Assert.Equal("b.jpg", viewModel.StatusMessage);
        Assert.False(service.LoadCancelled);
    }

    private sealed class StreamingService : IPreviewGenerationService
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool LoadCancelled { get; private set; }
        public async IAsyncEnumerable<PreviewPhoto> LoadPhotosAsync(string folderPath, bool includeSubfolders,
            IProgress<PreviewLoadProgress>? progress, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new("a.jpg"); yield return new("b.jpg");
            this.Waiting.TrySetResult();
            try { await this.Continue.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { this.LoadCancelled = true; throw; }
            yield return new("c.jpg");
            progress?.Report(new(3, 3, 3, "完了"));
        }
        public Task<PreviewGenerationResult> GenerateAsync(PreviewPhoto? photo, PreviewGenerationSettings settings, CancellationToken cancellationToken)
            => Task.FromResult(new PreviewGenerationResult(new byte[] { 1 }, null, photo!.DisplayName, true));
    }
}
