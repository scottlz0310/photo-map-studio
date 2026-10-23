using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.App.Tests.Services;

public sealed class IncompleteMapGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 通信失敗10件で無効時は中止し有効時は通信を止め全写真を警告付き保存する(bool allow)
    {
        using var tree = new PhotoTree();
        string[] files = Enumerable.Range(0, 15).Select(index => tree.Add($"{index:D2}.jpg")).ToArray();
        var source = new OfflineSource();
        using var style = new OpenMapTilesStyle();
        var composer = new SkiaMapImageComposer(new NoRaster(),
            new VectorMapRenderer(new CachedVectorTileSource(source, new EmptyCache()), style));
        var service = new BatchGenerationService(new StubPhotoFileEnumerator(files), new Reader(), composer);

        BatchGenerationSummary summary = await service.GenerateAsync(new()
        {
            InputFolderPath = tree.Root,
            OutputFolderPath = "maps",
            TileSource = TileSources.OpenFreeMap,
            Width = 256,
            Height = 256,
            Zoom = 1,
            AllowIncompleteVectorMaps = allow,
        }, null, CancellationToken.None);

        Assert.Equal(10, source.RequestCount);
        Assert.Equal(allow ? 15 : 10, summary.ProcessedCount);
        Assert.Equal(allow ? 15 : 0, summary.SuccessCount);
        Assert.Equal(allow ? 15 : 0, summary.WarningCount);
        Assert.Equal(allow ? 0 : 10, summary.ErrorCount);
        Assert.Equal(!allow, summary.StopReason is not null);
        string output = Path.Combine(tree.Root, "maps");
        if (allow) { Assert.Equal(15, Directory.GetFiles(output, "*.png").Length); }
        else { Assert.False(Directory.Exists(output)); }
        if (allow) { Assert.True(summary.Issues[^1].NetworkSuspended); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 警告画像は成功の内数として進捗と履歴に残し再読み込みでも識別できる(bool suspended)
    {
        using var tree = new PhotoTree();
        string photo = tree.Add("地区/写真.jpg");
        var warning = new IncompleteMapWarning(3, 4, suspended);
        var composer = new Composer(warning);
        var progress = new List<BatchGenerationProgress>();
        var service = new BatchGenerationService(new StubPhotoFileEnumerator([photo]), new Reader(), composer);

        BatchGenerationSummary summary = await service.GenerateAsync(new()
        {
            InputFolderPath = tree.Root,
            OutputFolderPath = "maps",
            TileSource = TileSources.OpenFreeMap,
            AllowIncompleteVectorMaps = true,
        }, new SynchronousProgress<BatchGenerationProgress>(progress.Add), CancellationToken.None);

        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.Equal(1, summary.ProcessedCount);
        Assert.Equal(0, summary.SkippedCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Null(summary.StopReason);
        Assert.True(File.Exists(Path.Combine(tree.Root, "地区", "maps", "写真_map.png")));
        Assert.True(Assert.Single(composer.Requests).AllowIncompleteVectorMaps);
        Assert.True(composer.Requests[0].TileSession?.AllowIncompleteVectorMaps);
        BatchGenerationProgress completed = Assert.Single(progress, item => !item.IsActivity);
        Assert.Equal("WARNING", completed.StatusText);
        Assert.Equal(1, completed.WarningCount);
        Assert.Contains(warning.Message, completed.Message, StringComparison.Ordinal);

        var store = new BatchGenerationHistoryStore(Path.Combine(tree.Root, "history"));
        await store.SaveAsync(BatchIssueHistoryTests.History(summary, tree.Root));
        BatchGenerationHistory history = Assert.Single(await store.LoadAsync());
        Assert.Equal(1, history.WarningCount);
        Assert.Equal(summary.Issues[0], Assert.Single(history.Issues));
        var displayed = new BatchHistoryViewModel(history);
        Assert.Contains("警告 1", displayed.SummaryText, StringComparison.Ordinal);
        BatchIssueViewModel issue = Assert.Single(displayed.Issues);
        Assert.Contains("警告", issue.StatusText, StringComparison.Ordinal);
        Assert.Contains("3/4", issue.ReasonText, StringComparison.Ordinal);
        Assert.Equal(suspended, issue.ReasonText.Contains("通信停止", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task プレビューへ設定を伝え生成画像と欠落警告を返す(bool allow)
    {
        using var tree = new PhotoTree();
        string photo = tree.Add("写真.jpg");
        var warning = allow ? new IncompleteMapWarning(2, 4, false) : null;
        var composer = new Composer(warning);
        var service = new PreviewGenerationService(new StubPhotoFileEnumerator([photo]), new Reader(), composer);
        PreviewGenerationResult result = await service.GenerateAsync(new PreviewPhoto(photo), new()
        {
            AllowIncompleteVectorMaps = allow,
            SelectedTileSource = TileSourceChoices.OpenFreeMap,
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Image.IsEmpty);
        Assert.Equal(allow, Assert.Single(composer.Requests).AllowIncompleteVectorMaps);
        if (warning is not null) { Assert.Contains(warning.Message, result.Message, StringComparison.Ordinal); }
        else { Assert.DoesNotContain("警告", result.Message, StringComparison.Ordinal); }
    }

    private sealed class Reader : IExifGpsReader
    {
        public GeoCoordinate? Read(string filePath) => new(35, 139);
    }

    private sealed class OfflineSource : IVectorTileSource
    {
        public string SourceId => "offline";
        public int MaxZoom => 1;
        public int RequestCount { get; private set; }
        public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
        {
            this.RequestCount++;
            session?.RecordRequest();
            throw new TileFetchException("通信断", new Uri("https://example.test/tile"), null, new HttpRequestException("通信断"));
        }
    }

    private sealed class EmptyCache : ITileCache
    {
        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoRaster : ITileProvider
    {
        public Task<byte[]> GetTileAsync(TileSource source, int zoom, int x, int y, CancellationToken cancellationToken, TileFetchSession? session = null)
            => throw new InvalidOperationException("ラスターは使用しません。");
    }

    private sealed class Composer(IncompleteMapWarning? warning) : IMapImageComposer
    {
        public List<MapCompositionRequest> Requests { get; } = [];
        public Task<MapCompositionResult> ComposeAsync(MapCompositionRequest request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            return Task.FromResult(new MapCompositionResult(new byte[] { 1 }, request.TileSource, false) { Warning = warning });
        }
    }
}
