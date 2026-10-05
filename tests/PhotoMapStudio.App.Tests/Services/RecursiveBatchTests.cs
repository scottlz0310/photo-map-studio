using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Tests.TestSupport;

namespace PhotoMapStudio.App.Tests.Services;

public class RecursiveBatchTests
{
    [Theory]
    [InlineData(218)]
    [InlineData(219)]
    [InlineData(255)]
    public async Task 長い最終出力名でも一時保存と移動が成功する(int length)
    {
        using var tree = new PhotoTree(); tree.Add("a.jpg");
        var settings = new BatchGenerationSettings
        {
            InputFolderPath = tree.Root,
            OutputFolderPath = "maps",
            OutputFilePrefix = new string('p', length - 5),
            OutputFilePostfix = string.Empty,
        };
        var service = new BatchGenerationService(new PhotoFileEnumerator(), new Reader(_ => new(35, 139)), new Composer());
        BatchGenerationSummary result = await service.GenerateAsync(settings, null, CancellationToken.None);
        Assert.Equal(1, result.SuccessCount); Assert.Equal(0, result.ErrorCount);
        string output = Assert.Single(Directory.EnumerateFiles(Path.Combine(tree.Root, "maps")));
        Assert.Equal(length, Path.GetFileName(output).Length);
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.EnumerateFiles(tree.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5200)]
    public async Task 規模別に相対出力し件数と一時ファイルの後始末を検証する(int count)
    {
        using var tree = new PhotoTree();
        tree.Populate(count);
        var clock = new FixedTimeProvider(DateTimeOffset.UnixEpoch);
        int readCount = 0;
        var service = new BatchGenerationService(new PhotoFileEnumerator(),
            new Reader(_ => ++readCount % 5 == 0 ? null : new GeoCoordinate(35.68, 139.76)),
            new Composer(_ => clock.Advance(TimeSpan.FromSeconds(1))), clock);
        var logs = new List<BatchGenerationProgress>();
        BatchGenerationSummary summary = await service.GenerateAsync(new()
        {
            InputFolderPath = tree.Root,
            OutputFolderPath = "maps",
            IncludeSubfolders = true,
            OutputFilePrefix = "map_",
            OutputFilePostfix = "_z16",
        }, new SynchronousProgress<BatchGenerationProgress>(logs.Add), CancellationToken.None);
        Assert.Equal(count, summary.ProcessedCount);
        Assert.Equal(count / 5, summary.SkippedCount);
        Assert.Equal(count - count / 5, summary.SuccessCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(TimeSpan.FromSeconds(summary.SuccessCount), summary.Elapsed);
        Assert.Equal(summary.SuccessCount, Directory.EnumerateFiles(tree.Root, "map_*_z16.png", SearchOption.AllDirectories).Count());
        Assert.Empty(Directory.EnumerateFiles(tree.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.Contains(logs, log => log.IsEnumerating);
        Assert.Equal(count, logs.Last(log => !log.IsActivity).Index);
        Assert.All(logs.Where(log => !log.IsEnumerating), log => Assert.False(Path.IsPathFullyQualified(log.FileName)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 同名写真は相対出力なら共存し絶対出力なら事前中止する(bool relative)
    {
        using var tree = new PhotoTree();
        tree.Add(Path.Combine("春", "a.jpg")); tree.Add(Path.Combine("秋", "a.jpg"));
        var composer = new Composer();
        var service = new BatchGenerationService(new PhotoFileEnumerator(), new Reader(_ => new(35, 139)), composer);
        var settings = new BatchGenerationSettings
        {
            InputFolderPath = tree.Root,
            IncludeSubfolders = true,
            OutputFolderPath = relative ? "." : Path.Combine(tree.Root, "absolute")
        };
        if (relative)
        {
            Assert.Equal(2, (await service.GenerateAsync(settings, null, CancellationToken.None)).SuccessCount);
            Assert.True(File.Exists(Path.Combine(tree.Root, "春", "a_map.png")));
            Assert.True(File.Exists(Path.Combine(tree.Root, "秋", "a_map.png")));
        }
        else
        {
            BatchGenerationException error = await Assert.ThrowsAsync<BatchGenerationException>(() => service.GenerateAsync(settings, null, CancellationToken.None));
            Assert.Contains("春", error.Message, StringComparison.Ordinal); Assert.Contains("秋", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, composer.Count);
        }
    }

    [Theory]
    [InlineData("maps")]
    [InlineData("out/map")]
    public async Task 相対出力フォルダを作れない写真をエラーにして別フォルダへ続行する(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var tree = new PhotoTree();
        tree.Add(Path.Combine("bad", "a.jpg")); tree.Add(Path.Combine("good", "b.jpg"));
        tree.Add(Path.Combine("bad", output.Split('/')[0]));
        var service = new BatchGenerationService(new PhotoFileEnumerator(), new Reader(_ => new(35, 139)), new Composer());
        BatchGenerationSummary result = await service.GenerateAsync(new() { InputFolderPath = tree.Root, IncludeSubfolders = true, OutputFolderPath = output }, null, CancellationToken.None);
        Assert.Equal(1, result.ErrorCount); Assert.Equal(1, result.SuccessCount);
        Assert.True(File.Exists(Path.Combine(tree.Root, "good", output, "b_map.png")));
    }

    [Theory]
    [InlineData(99, true)]
    [InlineData(100, true)]
    [InlineData(101, true)]
    [InlineData(101, false)]
    public async Task カスタム大量実行は列挙後に確認し拒否なら生成しない(int count, bool approved)
    {
        using var tree = new PhotoTree(); tree.Populate(count);
        var composer = new Composer(); int confirmations = 0;
        var service = new BatchGenerationService(new PhotoFileEnumerator(), new Reader(_ => new(35, 139)), composer);
        BatchGenerationSummary result = await service.GenerateAsync(new()
        {
            InputFolderPath = tree.Root,
            OutputFolderPath = "maps",
            IncludeSubfolders = true,
            TileSource = new("カスタム", "https://example.com/{z}/{x}/{y}.png", 0, 19, "出典", TileRateLimit.Conservative),
            ConfirmLargeBatchAsync = (actual, _) => { Assert.Equal(count, actual); confirmations++; return Task.FromResult(approved); },
        }, null, CancellationToken.None);
        Assert.Equal(count > 100 ? 1 : 0, confirmations);
        Assert.Equal(count > 100 && !approved ? 0 : count, composer.Count);
        Assert.Equal(count > 100 && !approved, result.IsCancelled);
    }

    [Theory]
    [InlineData("enumeration")]
    [InlineData("exif")]
    [InlineData("composition")]
    public async Task 各フェーズのキャンセルで次の写真へ進まず一時ファイルを残さない(string phase)
    {
        using var tree = new PhotoTree(); tree.Populate(100);
        using var cancellation = new CancellationTokenSource();
        var reader = new Reader(_ => { if (phase == "exif") { cancellation.Cancel(); } return new(35, 139); });
        var composer = new Composer(_ => { if (phase == "composition") { cancellation.Cancel(); } });
        var service = new BatchGenerationService(new PhotoFileEnumerator(), reader, composer);
        BatchGenerationSummary result = await service.GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps", IncludeSubfolders = true },
            new SynchronousProgress<BatchGenerationProgress>(value => { if (phase == "enumeration" && value.IsEnumerating) { cancellation.Cancel(); } }), cancellation.Token);
        Assert.True(result.IsCancelled); Assert.Equal(0, result.SuccessCount);
        Assert.InRange(composer.Count, 0, 1);
        Assert.Empty(Directory.EnumerateFiles(tree.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task 連続タイル失敗の閾値で中止し次の写真を生成しない(int status)
    {
        using var tree = new PhotoTree(); tree.Populate(100);
        int requests = 0;
        var composer = new Composer(request =>
        {
            requests++;
            var failure = new TileFetchException($"HTTP {status} / 配信元 example.com / Retry-After: 60", new Uri("https://example.com/0/0/0.png"), (System.Net.HttpStatusCode)status, null);
            request.TileSession!.RecordRequest(); request.TileSession.RecordFailure(failure); request.TileSession.ThrowIfStopped(); throw failure;
        });
        var service = new BatchGenerationService(new PhotoFileEnumerator(), new Reader(_ => new(35, 139)), composer);
        BatchGenerationSummary result = await service.GenerateAsync(new() { InputFolderPath = tree.Root, OutputFolderPath = "maps", IncludeSubfolders = true }, null, CancellationToken.None);
        Assert.Equal(TileFetchSession.FailureLimit, requests);
        Assert.Equal(TileFetchSession.FailureLimit, result.ErrorCount);
        Assert.Contains($"HTTP {status}", result.StopReason!, StringComparison.Ordinal);
        Assert.Contains("Retry-After: 60", result.StopReason!, StringComparison.Ordinal);
        Assert.Equal(requests, result.Tiles!.NetworkCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task キャンセル時に完成済みPNGを保持し再実行では最初から上書きする(int stopAfter)
    {
        using var tree = new PhotoTree(); tree.Populate(20);
        using var cancellation = new CancellationTokenSource();
        var composer = new Composer();
        var service = new BatchGenerationService(new PhotoFileEnumerator(), new Reader(_ => new(35, 139)), composer);
        var settings = new BatchGenerationSettings { InputFolderPath = tree.Root, OutputFolderPath = "maps", IncludeSubfolders = true };
        BatchGenerationSummary cancelled = await service.GenerateAsync(settings, new SynchronousProgress<BatchGenerationProgress>(value =>
        {
            if (!value.IsActivity && !value.IsEnumerating && value.Status == BatchGenerationStatus.Success && value.Index == stopAfter) { cancellation.Cancel(); }
        }), cancellation.Token);
        Assert.True(cancelled.IsCancelled); Assert.Equal(stopAfter, cancelled.ProcessedCount);
        Assert.Equal(stopAfter, Directory.EnumerateFiles(tree.Root, "*_map.png", SearchOption.AllDirectories).Count());
        Assert.Empty(Directory.EnumerateFiles(tree.Root, "*.tmp", SearchOption.AllDirectories));
        BatchGenerationSummary repeated = await service.GenerateAsync(settings, null, CancellationToken.None);
        Assert.Equal(20, repeated.SuccessCount); Assert.Equal(stopAfter + 20, composer.Count);
    }

    private sealed class Reader(Func<string, GeoCoordinate?> read) : IExifGpsReader
    {
        public GeoCoordinate? Read(string filePath) => read(filePath);
    }
    private sealed class Composer(Action<MapCompositionRequest>? action = null) : IMapImageComposer
    {
        public int Count { get; private set; }
        public Task<MapCompositionResult> ComposeAsync(MapCompositionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); this.Count++; action?.Invoke(request);
            return Task.FromResult(new MapCompositionResult(new byte[] { 1 }, request.TileSource, false));
        }
    }
}
