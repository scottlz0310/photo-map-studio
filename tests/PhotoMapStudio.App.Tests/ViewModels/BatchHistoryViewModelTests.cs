using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.Tests.Services;
using PhotoMapStudio.App.ViewModels;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.App.Tests.ViewModels;

public class BatchHistoryViewModelTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task 進捗通知を受信しなくても確定結果を自動保存し表示する(bool cancelled, bool stopped, bool setupFailure)
    {
        using var tree = new PhotoTree();
        var summary = new BatchGenerationSummary(2, 1, 4, cancelled)
        {
            ErrorCount = 1,
            StopReason = stopped || setupFailure ? "secret-key-を含む停止メッセージ" : null,
            Issues = [new(3, "下位\\同名.jpg", BatchIssueTarget.Photo, BatchGenerationStatus.Skip, BatchIssueReason.MissingGps)],
        };
        var store = new Store();
        var model = new MainViewModel(new Settings(), batchGenerationService: new Batch(summary, setupFailure), historyStore: store)
        { InputFolderPath = tree.Root, OutputFolderPath = "maps" };

        await model.GenerateCommand.ExecuteAsync(null);

        BatchGenerationHistory saved = Assert.Single(store.Entries);
        Assert.Equal(summary.Issues, saved.Issues);
        Assert.Equal(cancelled, saved.IsCancelled);
        Assert.Equal(stopped || setupFailure, saved.IsStopped);
        Assert.Equal(tree.Root, saved.InputFolderPath);
        Assert.Equal("下位\\同名.jpg", Assert.Single(model.SelectedBatchHistory!.Issues).RelativePath);
        Assert.Contains("成功 2", model.GenerationSummary, StringComparison.Ordinal);
        Assert.Empty(model.GenerationLogs);
        Assert.Contains("自動保存しました", model.BatchHistoryMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 履歴の入出力失敗を通知し処理結果を失わない(bool load)
    {
        using var tree = new PhotoTree();
        var summary = new BatchGenerationSummary(1, 0, 1, false);
        var store = new Store { Fail = true };
        var model = new MainViewModel(new Settings(), batchGenerationService: new Batch(summary, false), historyStore: store)
        { InputFolderPath = tree.Root, OutputFolderPath = "maps" };
        if (load) { await model.LoadBatchHistoryCommand.ExecuteAsync(null); }
        else
        {
            await model.GenerateCommand.ExecuteAsync(null);
            Assert.Equal(1, model.SelectedBatchHistory!.History.SuccessCount);
            Assert.Equal(0, model.SelectedBatchHistory.History.ErrorCount);
            Assert.Same(model.SelectedBatchHistory, Assert.Single(model.BatchHistories));
        }
        Assert.Contains("拒否", model.BatchHistoryMessage, StringComparison.Ordinal);
        Assert.False(model.IsHistoryBusy);
        Assert.False(model.IsGenerating);
    }

    [Fact]
    public async Task 保存済み実行を選び直して相対パスと理由を確認できる()
    {
        var store = new Store();
        store.Entries.Add(BatchIssueHistoryTests.History(new(1, 0, 1, false), "C:\\以前"));
        store.Entries.Add(BatchIssueHistoryTests.History(new(0, 1, 1, false)
        { Issues = [new(1, "別\\photo.jpg", BatchIssueTarget.Photo, BatchGenerationStatus.Skip, BatchIssueReason.TileNotFound, 404)] }, "C:\\今回"));
        var model = new MainViewModel(new Settings(), historyStore: store);
        await model.LoadBatchHistoryCommand.ExecuteAsync(null);
        model.SelectedBatchHistory = model.BatchHistories[1];
        Assert.Equal("C:\\今回", model.SelectedBatchHistory.InputFolderPath);
        Assert.Contains("HTTP 404", Assert.Single(model.SelectedBatchHistory.Issues).ReasonText, StringComparison.Ordinal);
        Assert.Equal("別\\photo.jpg", model.SelectedBatchHistory.Issues[0].RelativePath);
    }

    [Fact]
    public async Task 確定結果の保存中は再実行や写真処理のキャンセルを受け付けない()
    {
        using var tree = new PhotoTree();
        var store = new BlockingStore();
        var model = new MainViewModel(new Settings(), batchGenerationService: new Batch(new(1, 0, 1, false), false), historyStore: store)
        { InputFolderPath = tree.Root, OutputFolderPath = "maps" };
        Task execution = model.GenerateCommand.ExecuteAsync(null);
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.GenerateCommand.CanExecute(null));
        Assert.False(model.CancelGenerationCommand.CanExecute(null));
        Assert.False(model.LoadBatchHistoryCommand.CanExecute(null));
        store.Completed.SetResult();
        await execution.ConfigureAwait(true);
        Assert.True(model.GenerateCommand.CanExecute(null));
        Assert.True(model.LoadBatchHistoryCommand.CanExecute(null));
    }

    private sealed class Settings : IPhotoMapSettingsRepository
    {
        public PhotoMapSettings Load() => new();
        public void Save(PhotoMapSettings settings) { }
    }

    private sealed class Batch(BatchGenerationSummary summary, bool fail) : IBatchGenerationService
    {
        public Task<BatchGenerationSummary> GenerateAsync(BatchGenerationSettings settings,
            IProgress<BatchGenerationProgress>? progress, CancellationToken cancellationToken)
            => fail ? Task.FromException<BatchGenerationSummary>(new BatchGenerationException("開始失敗") { Summary = summary })
                : Task.FromResult(summary);
    }

    private sealed class Store : IBatchGenerationHistoryStore
    {
        public List<BatchGenerationHistory> Entries { get; } = [];
        public bool Fail { get; init; }
        public Task<IReadOnlyList<BatchGenerationHistory>> LoadAsync()
            => this.Fail ? Task.FromException<IReadOnlyList<BatchGenerationHistory>>(new IOException("読み込み拒否"))
                : Task.FromResult<IReadOnlyList<BatchGenerationHistory>>(this.Entries.ToArray());
        public Task SaveAsync(BatchGenerationHistory history)
        {
            if (this.Fail) { return Task.FromException(new IOException("保存拒否")); }
            this.Entries.Add(history);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingStore : IBatchGenerationHistoryStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<BatchGenerationHistory>> LoadAsync() => Task.FromResult<IReadOnlyList<BatchGenerationHistory>>([]);
        public async Task SaveAsync(BatchGenerationHistory history)
        {
            this.Started.SetResult();
            await this.Completed.Task.ConfigureAwait(false);
        }
    }
}
