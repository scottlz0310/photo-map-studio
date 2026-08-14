using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.App.Models;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// Core の写真解析・地図合成契約をプレビュー用の UI 契約へつなぐ。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "DI コンテナーから生成されるプレビューサービス。")]
public sealed class PreviewGenerationService : IPreviewGenerationService
{
    private const string InvalidImageSizeMessage = "画像サイズ(幅・高さ)は正の整数を指定してください。";
    private const string InvalidZoomMessage = "ズームレベルは 1 〜 19 の範囲で指定してください。";

    private readonly IPhotoFileEnumerator photoFileEnumerator;
    private readonly IExifGpsReader exifGpsReader;
    private readonly IMapImageComposer mapImageComposer;

    /// <summary>
    /// サービスを構築する。
    /// </summary>
    /// <param name="photoFileEnumerator">写真列挙器。</param>
    /// <param name="exifGpsReader">GPS 読み取り器。</param>
    /// <param name="mapImageComposer">地図合成器。</param>
    public PreviewGenerationService(
        IPhotoFileEnumerator photoFileEnumerator,
        IExifGpsReader exifGpsReader,
        IMapImageComposer mapImageComposer)
    {
        this.photoFileEnumerator = photoFileEnumerator ?? throw new ArgumentNullException(nameof(photoFileEnumerator));
        this.exifGpsReader = exifGpsReader ?? throw new ArgumentNullException(nameof(exifGpsReader));
        this.mapImageComposer = mapImageComposer ?? throw new ArgumentNullException(nameof(mapImageComposer));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreviewPhoto>> LoadPhotosAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return [];
        }

        IReadOnlyList<string> filePaths = await Task.Run(
            () => this.photoFileEnumerator.Enumerate(folderPath),
            cancellationToken).ConfigureAwait(false);

        var photos = new List<PreviewPhoto>();
        foreach (string filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GeoCoordinate? coordinate = await this.ReadGpsAsync(filePath, cancellationToken)
                .ConfigureAwait(false);
            if (coordinate is not null)
            {
                photos.Add(new PreviewPhoto(filePath));
            }
        }

        return photos;
    }

    /// <inheritdoc />
    public async Task<PreviewGenerationResult> GenerateAsync(
        PreviewPhoto? photo,
        PreviewGenerationSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!TryGetPositiveInteger(settings.Width, out int width)
            || !TryGetPositiveInteger(settings.Height, out int height))
        {
            return Failure(InvalidImageSizeMessage);
        }

        if (!TryGetInteger(settings.Zoom, out int zoom) || zoom is < 1 or > 19)
        {
            return Failure(InvalidZoomMessage);
        }

        if (photo is null)
        {
            return Failure("プレビュー対象の画像が選択されていません。");
        }

        if (!File.Exists(photo.FilePath))
        {
            return Failure("指定された画像ファイルが存在しません。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        GeoCoordinate? coordinate = await this.ReadGpsAsync(photo.FilePath, cancellationToken)
            .ConfigureAwait(false);
        if (coordinate is null)
        {
            return Failure("選択された写真にGPS情報が含まれていません。");
        }

        TileSource tileSource;
        try
        {
            tileSource = settings.CreateTileSource();
        }
        catch (ArgumentException exception)
        {
            return Failure($"プレビュー生成エラー: {exception.Message}", coordinate);
        }

        if (!tileSource.SupportsZoom(zoom))
        {
            return Failure(
                $"選択中のタイルソースではズームレベルは {tileSource.MinZoom} 〜 {tileSource.MaxZoom} の範囲で指定してください。",
                coordinate);
        }

        try
        {
            MapCompositionResult composition = await this.mapImageComposer.ComposeAsync(
                new MapCompositionRequest
                {
                    Center = coordinate.Value,
                    TileSource = tileSource,
                    Width = width,
                    Height = height,
                    Zoom = zoom,
                    PinImagePath = PhotoMapAssetPaths.ResolvePinImagePath(settings.PinImagePath),
                    AllowWorldwideFallback = true,
                },
                cancellationToken).ConfigureAwait(false);

            string message = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"緯度: {coordinate.Value.Latitude:F5}, 経度: {coordinate.Value.Longitude:F5}");
            return new PreviewGenerationResult(composition.Png, coordinate, message, Succeeded: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is MapCompositionException
            or TileFetchException
            or ArgumentException
            or InvalidOperationException
            or IOException)
        {
            return Failure($"プレビュー生成エラー: {exception.Message}", coordinate);
        }
    }

    private Task<GeoCoordinate?> ReadGpsAsync(string filePath, CancellationToken cancellationToken)
        => Task.Run(() => this.exifGpsReader.Read(filePath), cancellationToken);

    private static PreviewGenerationResult Failure(string message, GeoCoordinate? coordinate = null)
        => new(ReadOnlyMemory<byte>.Empty, coordinate, message, Succeeded: false);

    private static bool TryGetPositiveInteger(double value, out int result)
    {
        if (TryGetInteger(value, out result) && result > 0)
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryGetInteger(double value, out int result)
    {
        if (double.IsFinite(value)
            && value >= int.MinValue
            && value <= int.MaxValue
            && value == Math.Truncate(value))
        {
            result = (int)value;
            return true;
        }

        result = 0;
        return false;
    }
}
