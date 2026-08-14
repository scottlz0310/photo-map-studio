using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

using PhotoMapStudio.App.Models;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Photos;
using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// 写真フォルダから地図画像を決定的な順序で一括生成する。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "DI コンテナーから生成される一括生成サービス。")]
public sealed class BatchGenerationService : IBatchGenerationService
{
    private const string OutsideCoverageMessage = "選択中のタイルソースは撮影地点を配信していません。タイルソースを OpenStreetMap に変更して再実行してください。";

    private readonly IPhotoFileEnumerator photoFileEnumerator;
    private readonly IExifGpsReader exifGpsReader;
    private readonly IMapImageComposer mapImageComposer;

    /// <summary>
    /// サービスを構築する。
    /// </summary>
    /// <param name="photoFileEnumerator">写真列挙器。</param>
    /// <param name="exifGpsReader">GPS 読み取り器。</param>
    /// <param name="mapImageComposer">地図合成器。</param>
    public BatchGenerationService(
        IPhotoFileEnumerator photoFileEnumerator,
        IExifGpsReader exifGpsReader,
        IMapImageComposer mapImageComposer)
    {
        this.photoFileEnumerator = photoFileEnumerator ?? throw new ArgumentNullException(nameof(photoFileEnumerator));
        this.exifGpsReader = exifGpsReader ?? throw new ArgumentNullException(nameof(exifGpsReader));
        this.mapImageComposer = mapImageComposer ?? throw new ArgumentNullException(nameof(mapImageComposer));
    }

    /// <inheritdoc />
    public async Task<BatchGenerationSummary> GenerateAsync(
        BatchGenerationSettings settings,
        IProgress<BatchGenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.InputFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.OutputFolderPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Height);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.Zoom);
        ArgumentNullException.ThrowIfNull(settings.TileSource);

        string[] files = await Task.Run(
            () => this.photoFileEnumerator.Enumerate(settings.InputFolderPath)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray(),
            cancellationToken).ConfigureAwait(false);

        if (files.Length == 0)
        {
            return new BatchGenerationSummary(0, 0, 0, IsCancelled: false);
        }

        IReadOnlyList<string> collisions = FindOutputCollisions(files);
        if (collisions.Count > 0)
        {
            throw new BatchGenerationException(
                $"出力ファイル名が衝突しています: {string.Join(", ", collisions)}");
        }

        try
        {
            Directory.CreateDirectory(settings.OutputFolderPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new BatchGenerationException(
                $"出力フォルダを作成できません: {exception.Message}",
                exception);
        }

        int successCount = 0;
        int skippedCount = 0;

        for (int index = 0; index < files.Length; index++)
        {
            string filePath = files[index];
            string fileName = Path.GetFileName(filePath);
            int displayIndex = index + 1;

            if (cancellationToken.IsCancellationRequested)
            {
                Report(progress, displayIndex, files.Length, fileName, BatchGenerationStatus.Cancelled, "処理が手動でキャンセルされました。");
                return new BatchGenerationSummary(successCount, skippedCount, files.Length, IsCancelled: true);
            }

            GeoCoordinate? coordinate;
            try
            {
                coordinate = await Task.Run(
                    () => this.exifGpsReader.Read(filePath),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Report(progress, displayIndex, files.Length, fileName, BatchGenerationStatus.Cancelled, "処理が手動でキャンセルされました。");
                return new BatchGenerationSummary(successCount, skippedCount, files.Length, IsCancelled: true);
            }
            catch (Exception exception) when (exception is ExifGpsReadException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException)
            {
                ReportError(progress, displayIndex, files.Length, fileName, exception);
                continue;
            }

            if (coordinate is null)
            {
                skippedCount++;
                Report(progress, displayIndex, files.Length, fileName, BatchGenerationStatus.Skip, "GPS情報が見つかりません。");
                continue;
            }

            try
            {
                MapCompositionResult composition = await this.mapImageComposer.ComposeAsync(
                    new MapCompositionRequest
                    {
                        Center = coordinate.Value,
                        TileSource = settings.TileSource,
                        Width = settings.Width,
                        Height = settings.Height,
                        Zoom = settings.Zoom,
                        PinImagePath = string.IsNullOrWhiteSpace(settings.PinImagePath)
                            ? null
                            : settings.PinImagePath.Trim(),
                        AllowWorldwideFallback = false,
                    },
                    cancellationToken).ConfigureAwait(false);

                string outputFileName = GetOutputFileName(filePath);
                string outputPath = Path.Combine(settings.OutputFolderPath, outputFileName);
                await WriteOutputAsync(outputPath, composition.Png, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                successCount++;
                string message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"位置情報 ({coordinate.Value.Latitude:F5}, {coordinate.Value.Longitude:F5}) -> {outputFileName} を作成しました。");
                Report(progress, displayIndex, files.Length, fileName, BatchGenerationStatus.Success, message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Report(progress, displayIndex, files.Length, fileName, BatchGenerationStatus.Cancelled, "処理が手動でキャンセルされました。");
                return new BatchGenerationSummary(successCount, skippedCount, files.Length, IsCancelled: true);
            }
            catch (TileFetchException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                skippedCount++;
                Report(progress, displayIndex, files.Length, fileName, BatchGenerationStatus.Skip, OutsideCoverageMessage);
            }
            catch (Exception exception) when (exception is MapCompositionException
                or TileFetchException
                or ArgumentException
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                ReportError(progress, displayIndex, files.Length, fileName, exception);
            }
        }

        return new BatchGenerationSummary(successCount, skippedCount, files.Length, IsCancelled: false);
    }

    private static IReadOnlyList<string> FindOutputCollisions(IReadOnlyList<string> files)
        => [.. files
            .GroupBy(GetOutputFileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

    private static string GetOutputFileName(string filePath)
        => $"{Path.GetFileNameWithoutExtension(filePath)}_map.png";

    private static async Task WriteOutputAsync(
        string outputPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        string temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ReportError(
        IProgress<BatchGenerationProgress>? progress,
        int index,
        int total,
        string fileName,
        Exception exception)
        => Report(
            progress,
            index,
            total,
            fileName,
            BatchGenerationStatus.Error,
            $"生成エラー: {exception.Message}");

    private static void Report(
        IProgress<BatchGenerationProgress>? progress,
        int index,
        int total,
        string fileName,
        BatchGenerationStatus status,
        string message)
        => progress?.Report(new BatchGenerationProgress(index, total, fileName, status, message));
}
