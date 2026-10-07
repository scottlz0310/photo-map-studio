using System.Net;

using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.App.Tests.Services;

public class BatchIssueHistoryTests
{
    [Theory]
    [InlineData("gpsなし", BatchIssueReason.MissingGps, BatchGenerationStatus.Skip, null)]
    [InlineData("gps読取", BatchIssueReason.GpsReadFailed, BatchGenerationStatus.Error, null)]
    [InlineData("404", BatchIssueReason.TileNotFound, BatchGenerationStatus.Skip, 404)]
    [InlineData("500", BatchIssueReason.MapGenerationFailed, BatchGenerationStatus.Error, 500)]
    [InlineData("出力", BatchIssueReason.OutputSaveFailed, BatchGenerationStatus.Error, null)]
    public async Task 進捗通知なしでも写真の相対パスと理由を確定結果に残す(
        string scenario, BatchIssueReason reason, BatchGenerationStatus status, int? http)
    {
        using var tree = new PhotoTree();
        string file = tree.Add("地区/同名.jpg");
        if (scenario == "出力") { Directory.CreateDirectory(Path.Combine(tree.Root, "地区", "maps", "同名_map.png")); }
        Exception? mapFailure = http is int code ? new TileFetchException("secret-key-含有メッセージ",
            new Uri("https://example.com/tile?token=secret-key"), (HttpStatusCode)code, null) : null;
        var service = new BatchGenerationService(new StubPhotoFileEnumerator([file]),
            new Reader(_ => scenario switch
            {
                "gpsなし" => null,
                "gps読取" => throw new IOException("secret-key-含有メッセージ"),
                _ => new GeoCoordinate(35.68123, 139.76712),
            }), new Composer(mapFailure));

        BatchGenerationSummary summary = await service.GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps" }, null, CancellationToken.None);

        BatchGenerationIssue issue = Assert.Single(summary.Issues);
        Assert.Equal(Path.Combine("地区", "同名.jpg"), issue.RelativePath);
        Assert.Equal(1, issue.Index);
        Assert.Equal(BatchIssueTarget.Photo, issue.Target);
        Assert.Equal(reason, issue.Reason);
        Assert.Equal(status, issue.Status);
        Assert.Equal(http, issue.HttpStatusCode);
        string displayedReason = new BatchIssueViewModel(issue).ReasonText;
        Assert.DoesNotContain("secret-key", displayedReason, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(displayedReason));
        Assert.Equal(status == BatchGenerationStatus.Skip ? 1 : 0, summary.SkippedCount);
        Assert.Equal(status == BatchGenerationStatus.Error ? 1 : 0, summary.ErrorCount);

        string historyFolder = Path.Combine(tree.Root, "history");
        var store = new BatchGenerationHistoryStore(historyFolder);
        await store.SaveAsync(History(summary, tree.Root));
        string json = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(historyFolder, "*.json")));
        Assert.DoesNotContain("secret-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("35.68123", json, StringComparison.Ordinal);
        Assert.DoesNotContain("139.76712", json, StringComparison.Ordinal);
        Assert.Equal(issue, Assert.Single(Assert.Single(await store.LoadAsync()).Issues));
    }

    [Fact]
    public async Task フォルダ列挙の失敗も写真件数とは別に残す()
    {
        using var tree = new PhotoTree();
        var service = new BatchGenerationService(new PhotoFileEnumerator(_ => throw new UnauthorizedAccessException("拒否")),
            new Reader(_ => new(35, 139)), new Composer());
        BatchGenerationSummary summary = await service.GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps" }, null, CancellationToken.None);
        BatchGenerationIssue issue = Assert.Single(summary.Issues);
        Assert.Equal(BatchIssueTarget.Folder, issue.Target);
        Assert.Equal(BatchIssueReason.EnumerationFailed, issue.Reason);
        Assert.Equal(0, summary.ProcessedCount);
        Assert.Equal(typeof(UnauthorizedAccessException).FullName, issue.ExceptionType);
    }

    [Fact]
    public async Task キャンセルしてもそれ以前のスキップを残す()
    {
        using var tree = new PhotoTree();
        string missing = tree.Add("a.jpg"), waiting = tree.Add("b.jpg");
        var composer = new BlockingComposer();
        var service = new BatchGenerationService(new StubPhotoFileEnumerator([missing, waiting]),
            new Reader(path => path == missing ? null : new(35, 139)), composer);
        using var cancellation = new CancellationTokenSource();
        Task<BatchGenerationSummary> run = service.GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps" }, null, cancellation.Token);
        await composer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        BatchGenerationSummary summary = await run.ConfigureAwait(true);
        Assert.True(summary.IsCancelled);
        Assert.Equal("a.jpg", Assert.Single(summary.Issues).RelativePath);
        Assert.Equal(1, summary.ProcessedCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(33)]
    public async Task 再読み込みでも最新から直近30回のみを保持する(int count)
    {
        using var tree = new PhotoTree();
        string folder = Path.Combine(tree.Root, "history");
        Directory.CreateDirectory(folder);
        string unrelated = Path.Combine(folder, "notes.json");
        await File.WriteAllTextAsync(unrelated, "履歴以外のファイル");
        var store = new BatchGenerationHistoryStore(folder);
        var records = new List<BatchGenerationHistory>();
        for (int index = 0; index < count; index++)
        {
            BatchGenerationHistory record = History(new(1, 0, 1, false), tree.Root) with
            { FinishedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(index) };
            records.Add(record);
            await store.SaveAsync(record);
        }

        IReadOnlyList<BatchGenerationHistory> loaded = await new BatchGenerationHistoryStore(folder).LoadAsync();
        Assert.Equal(records.TakeLast(30).Reverse().Select(record => record.Id), loaded.Select(record => record.Id));
        Assert.Equal(Math.Min(count, 30), Directory.GetFiles(folder, "batch-*.json").Length);
        Assert.Equal("履歴以外のファイル", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task 大量写真の途中の唯一のスキップを最終ファイルと取り違えず保持する()
    {
        using var tree = new PhotoTree();
        string[] files = Enumerable.Range(0, 4961).Select(index => tree.Add($"地区/{index:D5}.jpg")).ToArray();
        var service = new BatchGenerationService(new StubPhotoFileEnumerator(files),
            new Reader(path => path == files[2480] ? null : new(35, 139)), new Composer());
        BatchGenerationSummary summary = await service.GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps" }, null, CancellationToken.None);
        Assert.Equal(4960, summary.SuccessCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(2481, Assert.Single(summary.Issues).Index);
        Assert.Equal(Path.Combine("地区", "02480.jpg"), summary.Issues[0].RelativePath);
        Assert.True(File.Exists(Path.Combine(tree.Root, "地区", "maps", "04960_map.png")));
    }

    [Fact]
    public async Task 連続失敗で中止した場合も処理済みの全エラーを保持する()
    {
        using var tree = new PhotoTree();
        string[] files = Enumerable.Range(0, 15).Select(index => tree.Add($"{index:D2}.jpg")).ToArray();
        var error = new TileFetchException("secret-key", new Uri("https://example.com/?token=secret-key"), HttpStatusCode.ServiceUnavailable, null);
        var service = new BatchGenerationService(new StubPhotoFileEnumerator(files), new Reader(_ => new(35, 139)), new Composer(error));
        BatchGenerationSummary summary = await service.GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps" }, null, CancellationToken.None);
        Assert.NotNull(summary.StopReason);
        Assert.Equal(TileFetchSession.FailureLimit, summary.ErrorCount);
        Assert.Equal(TileFetchSession.FailureLimit, summary.Issues.Count);
        Assert.All(summary.Issues, issue => Assert.Equal(503, issue.HttpStatusCode));
        Assert.Equal("09.jpg", summary.Issues[^1].RelativePath);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":1}")]
    [InlineData("壊れたJSON")]
    public async Task 不完全な履歴を成功扱いや自動削除しない(string json)
    {
        using var tree = new PhotoTree();
        string path = Path.Combine(tree.Root, "batch-broken.json");
        await File.WriteAllTextAsync(path, json);
        var store = new BatchGenerationHistoryStore(tree.Root);
        await Assert.ThrowsAsync<InvalidDataException>(store.LoadAsync);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(History(new(1, 0, 1, false), tree.Root)));
        Assert.Equal(json, await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(tree.Root));
    }

    [Fact]
    public async Task 履歴保存先がファイルの場合に空の履歴へ置き換えない()
    {
        using var tree = new PhotoTree();
        string path = tree.Add("履歴保存先");
        var store = new BatchGenerationHistoryStore(path);
        await Assert.ThrowsAsync<IOException>(store.LoadAsync);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(History(new(1, 0, 1, false), tree.Root)));
        Assert.True(File.Exists(path));
    }

    internal static BatchGenerationHistory History(BatchGenerationSummary summary, string input)
        => new(1, Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), input,
            summary.SuccessCount, summary.SkippedCount, summary.ErrorCount, summary.TotalCount,
            summary.IsCancelled, summary.StopReason is not null, summary.Elapsed, summary.Issues);

    private sealed class Reader(Func<string, GeoCoordinate?> read) : IExifGpsReader
    {
        public GeoCoordinate? Read(string filePath) => read(filePath);
    }

    private sealed class Composer(Exception? failure = null) : IMapImageComposer
    {
        public Task<MapCompositionResult> ComposeAsync(MapCompositionRequest request, CancellationToken cancellationToken)
        {
            if (failure is TileFetchException tile) { request.TileSession?.RecordFailure(tile); }
            return failure is null ? Task.FromResult(new MapCompositionResult(new byte[] { 1 }, request.TileSource, false))
                : Task.FromException<MapCompositionResult>(failure);
        }
    }

    private sealed class BlockingComposer : IMapImageComposer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MapCompositionResult> ComposeAsync(MapCompositionRequest request, CancellationToken cancellationToken)
        {
            this.Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("キャンセルされませんでした。");
        }
    }
}
