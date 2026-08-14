using System.Net;

using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Tests.Services;

public class BatchGenerationServiceTests
{
    [Fact]
    public async Task ファイル名順で処理しGPSなしをスキップして出力する()
    {
        using var fixture = new TestFixture();
        string[] files = [
            fixture.AddInputFile("z.jpg"),
            fixture.AddInputFile("no-gps.jpg"),
            fixture.AddInputFile("a.jpg"),
        ];
        var reader = new StubExifGpsReader(new Dictionary<string, GeoCoordinate?>
        {
            [files[0]] = new(35.68123, 139.76712),
            [files[1]] = null,
            [files[2]] = new(34.69374, 135.50218),
        });
        var composer = new RecordingMapImageComposer();
        var service = new BatchGenerationService(
            new StubPhotoFileEnumerator(files),
            reader,
            composer);
        var progress = new List<BatchGenerationProgress>();

        BatchGenerationSummary summary = await service.GenerateAsync(
            new BatchGenerationSettings
            {
                InputFolderPath = fixture.InputPath,
                OutputFolderPath = fixture.OutputPath,
            },
            new InlineProgress<BatchGenerationProgress>(progress.Add),
            CancellationToken.None);

        Assert.Equal(new BatchGenerationSummary(2, 1, 3, IsCancelled: false), summary);
        Assert.Equal(["a.jpg", "no-gps.jpg", "z.jpg"], progress.Select(item => item.FileName));
        Assert.Equal(
            [BatchGenerationStatus.Success, BatchGenerationStatus.Skip, BatchGenerationStatus.Success],
            progress.Select(item => item.Status));
        Assert.True(File.Exists(Path.Combine(fixture.OutputPath, "a_map.png")));
        Assert.True(File.Exists(Path.Combine(fixture.OutputPath, "z_map.png")));
        Assert.DoesNotContain(composer.Requests, request => request.AllowWorldwideFallback);
    }

    [Fact]
    public async Task 出力名の衝突を事前検出して出力を開始しない()
    {
        using var fixture = new TestFixture();
        string first = fixture.AddInputFile("photo.jpg");
        string second = fixture.AddInputFile("photo.heic");
        var service = new BatchGenerationService(
            new StubPhotoFileEnumerator([first, second]),
            new StubExifGpsReader(new Dictionary<string, GeoCoordinate?>()),
            new RecordingMapImageComposer());

        BatchGenerationException exception = await Assert.ThrowsAsync<BatchGenerationException>(() => service.GenerateAsync(
            new BatchGenerationSettings
            {
                InputFolderPath = fixture.InputPath,
                OutputFolderPath = fixture.OutputPath,
            },
            progress: null,
            CancellationToken.None));

        Assert.Contains("photo_map.png", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.OutputPath));
    }

    [Fact]
    public async Task 国外配信範囲外の404をSKIPとして記録する()
    {
        using var fixture = new TestFixture();
        string file = fixture.AddInputFile("outside.jpg");
        var progress = new List<BatchGenerationProgress>();
        var service = new BatchGenerationService(
            new StubPhotoFileEnumerator([file]),
            new StubExifGpsReader(new Dictionary<string, GeoCoordinate?>
            {
                [file] = new(40.7128, -74.0060),
            }),
            new ThrowingMapImageComposer(new TileFetchException(
                "404",
                new Uri("https://tiles.example.com/15/0/0.png"),
                HttpStatusCode.NotFound,
                innerException: null)));

        BatchGenerationSummary summary = await service.GenerateAsync(
            new BatchGenerationSettings
            {
                InputFolderPath = fixture.InputPath,
                OutputFolderPath = fixture.OutputPath,
            },
            new InlineProgress<BatchGenerationProgress>(progress.Add),
            CancellationToken.None);

        Assert.Equal(new BatchGenerationSummary(0, 1, 1, IsCancelled: false), summary);
        Assert.Equal(BatchGenerationStatus.Skip, Assert.Single(progress).Status);
        Assert.Contains("配信していません", progress[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task タイル合成中のキャンセルをCANCELLEDとして記録する()
    {
        using var fixture = new TestFixture();
        string file = fixture.AddInputFile("photo.jpg");
        var composer = new BlockingMapImageComposer();
        var service = new BatchGenerationService(
            new StubPhotoFileEnumerator([file]),
            new StubExifGpsReader(new Dictionary<string, GeoCoordinate?>
            {
                [file] = new(35.68123, 139.76712),
            }),
            composer);
        var progress = new List<BatchGenerationProgress>();
        using var cancellation = new CancellationTokenSource();

        Task<BatchGenerationSummary> generation = service.GenerateAsync(
            new BatchGenerationSettings
            {
                InputFolderPath = fixture.InputPath,
                OutputFolderPath = fixture.OutputPath,
            },
            new InlineProgress<BatchGenerationProgress>(progress.Add),
            cancellation.Token);

        await composer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        BatchGenerationSummary summary = await generation.ConfigureAwait(true);

        Assert.True(summary.IsCancelled);
        Assert.Equal(BatchGenerationStatus.Cancelled, Assert.Single(progress).Status);
        await composer.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"photo-map-batch-{Guid.NewGuid():N}");

        public TestFixture()
        {
            this.InputPath = Path.Combine(this.root, "input");
            this.OutputPath = Path.Combine(this.root, "output");
            Directory.CreateDirectory(this.InputPath);
        }

        public string InputPath { get; }

        public string OutputPath { get; }

        public string AddInputFile(string fileName)
        {
            string path = Path.Combine(this.InputPath, fileName);
            File.WriteAllBytes(path, [0x01]);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(this.root))
            {
                Directory.Delete(this.root, recursive: true);
            }
        }
    }

    private sealed class StubPhotoFileEnumerator(IReadOnlyList<string> files) : IPhotoFileEnumerator
    {
        public IReadOnlyList<string> Enumerate(string folderPath) => files;
    }

    private sealed class StubExifGpsReader(
        IReadOnlyDictionary<string, GeoCoordinate?> coordinates) : IExifGpsReader
    {
        public GeoCoordinate? Read(string filePath)
            => coordinates.TryGetValue(filePath, out GeoCoordinate? coordinate)
                ? coordinate
                : null;
    }

    private sealed class RecordingMapImageComposer : IMapImageComposer
    {
        public List<MapCompositionRequest> Requests { get; } = [];

        public Task<MapCompositionResult> ComposeAsync(
            MapCompositionRequest request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            return Task.FromResult(new MapCompositionResult(
                new byte[] { 0x89, 0x50, 0x4e, 0x47 },
                request.TileSource,
                UsedFallback: false));
        }
    }

    private sealed class ThrowingMapImageComposer(Exception exception) : IMapImageComposer
    {
        public Task<MapCompositionResult> ComposeAsync(
            MapCompositionRequest request,
            CancellationToken cancellationToken)
            => Task.FromException<MapCompositionResult>(exception);
    }

    private sealed class BlockingMapImageComposer : IMapImageComposer
    {
        public TaskCompletionSource Started { get; } = NewCompletionSource();

        public TaskCompletionSource CancellationObserved { get; } = NewCompletionSource();

        public async Task<MapCompositionResult> ComposeAsync(
            MapCompositionRequest request,
            CancellationToken cancellationToken)
        {
            this.Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this.CancellationObserved.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("キャンセルされずに合成が完了しました。");
        }

        private static TaskCompletionSource NewCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
