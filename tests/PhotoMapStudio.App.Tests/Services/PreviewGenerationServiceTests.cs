using PhotoMapStudio.App.Models;
using PhotoMapStudio.App.Services;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Tests.Services;

public class PreviewGenerationServiceTests
{
    [Fact]
    public async Task GPS情報を持つ写真だけを一覧にする()
    {
        var enumerator = new StubPhotoFileEnumerator(["gps.jpg", "without-gps.jpg"]);
        var reader = new StubExifGpsReader(new Dictionary<string, GeoCoordinate?>
        {
            ["gps.jpg"] = new(35.68123, 139.76712),
            ["without-gps.jpg"] = null,
        });
        var service = new PreviewGenerationService(
            enumerator,
            reader,
            new StubMapImageComposer());

        IReadOnlyList<PreviewPhoto> photos = await service.LoadPhotosAsync(
            "C:\\Photos",
            CancellationToken.None);

        var photo = Assert.Single(photos);
        Assert.Equal("gps.jpg", photo.DisplayName);
        Assert.Equal("gps.jpg", photo.FilePath);
    }

    [Fact]
    public async Task コンポーザーまでキャンセルを伝播する()
    {
        string filePath = Path.GetTempFileName();
        try
        {
            var composer = new BlockingMapImageComposer();
            var service = new PreviewGenerationService(
                new StubPhotoFileEnumerator([filePath]),
                new StubExifGpsReader(new Dictionary<string, GeoCoordinate?>
                {
                    [filePath] = new(35.68123, 139.76712),
                }),
                composer);
            using var cancellation = new CancellationTokenSource();

            Task<PreviewGenerationResult> generation = service.GenerateAsync(
                new PreviewPhoto(filePath),
                new PreviewGenerationSettings(),
                cancellation.Token);

            await composer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation);
            await composer.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            File.Delete(filePath);
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

    private sealed class StubMapImageComposer : IMapImageComposer
    {
        public Task<MapCompositionResult> ComposeAsync(
            MapCompositionRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(new MapCompositionResult(
                ReadOnlyMemory<byte>.Empty,
                request.TileSource,
                UsedFallback: false));
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
}
