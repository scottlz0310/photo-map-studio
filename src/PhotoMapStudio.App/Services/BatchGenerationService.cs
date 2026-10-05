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
    private const string OutsideCoverageMessage = "選択中のタイルソースは撮影地点を配信していません。一括取得・画像保存が許可された配信元をカスタム設定で指定して再実行してください。";

    private readonly IPhotoFileEnumerator photoFileEnumerator;
    private readonly IExifGpsReader exifGpsReader;
    private readonly IMapImageComposer mapImageComposer;
    private readonly TimeProvider timeProvider;
    private readonly TileTrafficController trafficController;

    /// <summary>
    /// サービスを構築する。
    /// </summary>
    /// <param name="photoFileEnumerator">写真列挙器。</param>
    /// <param name="exifGpsReader">GPS 読み取り器。</param>
    /// <param name="mapImageComposer">地図合成器。</param>
    public BatchGenerationService(
        IPhotoFileEnumerator photoFileEnumerator,
        IExifGpsReader exifGpsReader,
        IMapImageComposer mapImageComposer,
        TimeProvider? timeProvider = null, TileTrafficController? trafficController = null)
    {
        this.photoFileEnumerator = photoFileEnumerator ?? throw new ArgumentNullException(nameof(photoFileEnumerator));
        this.exifGpsReader = exifGpsReader ?? throw new ArgumentNullException(nameof(exifGpsReader));
        this.mapImageComposer = mapImageComposer ?? throw new ArgumentNullException(nameof(mapImageComposer));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.trafficController = trafficController ?? new TileTrafficController();
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

        OutputPathResolver.Validate(settings.OutputFolderPath, settings.OutputFilePrefix, settings.OutputFilePostfix);
        if (settings.TileSource.IsOfficialOpenStreetMap)
        {
            throw new BatchGenerationException("OSM公式サーバーはプレビュー用です。一括取得・画像保存が許可されたOSM系配信元をカスタム設定で指定してください。");
        }

        long started = this.timeProvider.GetTimestamp();
        var session = new TileFetchSession(this.timeProvider);
        int successCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        int total = 0;
        int currentIndex = 0;
        string currentFile = string.Empty;
        BatchGenerationSummary Summary(bool cancelled, string? stopReason = null)
            => new(successCount, skippedCount, total, cancelled)
            {
                ErrorCount = errorCount,
                Elapsed = this.timeProvider.GetElapsedTime(started),
                Tiles = session.Snapshot(),
                StopReason = stopReason,
            };
        void Notify(int index, string name, BatchGenerationStatus status, string message, bool activity = false)
            => progress?.Report(new(index, total, name, status, message)
            {
                IsActivity = activity,
                SuccessCount = successCount,
                SkippedCount = skippedCount,
                ErrorCount = errorCount,
                Elapsed = this.timeProvider.GetElapsedTime(started),
                Tiles = session.Snapshot(),
            });
        long lastActivity = started;
        session.Progress = new InlineProgress<TileFetchStatistics>(_ =>
        {
            long now = this.timeProvider.GetTimestamp();
            if (this.timeProvider.GetElapsedTime(lastActivity, now) < TimeSpan.FromMilliseconds(100)) { return; }
            lastActivity = now;
            Notify(currentIndex, currentFile, BatchGenerationStatus.Success, $"生成中: {currentFile}", activity: true);
        });

        string[] files;
        try
        {
            var scanProgress = new InlineProgress<PhotoEnumerationProgress>(scan => progress?.Report(
                new(0, 0, scan.RelativePath, scan.Error is null ? BatchGenerationStatus.Success : BatchGenerationStatus.Error,
                    scan.Error is null ? $"列挙: 走査済み {scan.FolderCount} フォルダ / 写真 {scan.PhotoCount} 枚" : $"フォルダ読み取りエラー: {scan.Error}")
                {
                    IsEnumerating = true,
                    IsActivity = scan.Error is null,
                    Elapsed = this.timeProvider.GetElapsedTime(started),
                }));
            files = await Task.Run(() => this.photoFileEnumerator.Enumerate(settings.InputFolderPath,
                settings.IncludeSubfolders, scanProgress, cancellationToken).ToArray(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Summary(cancelled: true);
        }

        total = files.Length;
        if (total == 0)
        {
            return Summary(cancelled: false);
        }

        var outputPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string photo in files)
        {
            if (cancellationToken.IsCancellationRequested) { return Summary(cancelled: true); }
            string output;
            try { output = OutputPathResolver.Resolve(photo, settings); }
            catch (ArgumentException) { continue; }
            if (!outputPaths.TryAdd(output, photo))
            {
                throw new BatchGenerationException($"出力パスが衝突しています: {output} / 元写真: {outputPaths[output]}, {photo}");
            }
        }

        bool isGsi = string.Equals(settings.TileSource.BuildTileUri(0, 0, 0).Host, "cyberjapandata.gsi.go.jp", StringComparison.OrdinalIgnoreCase);
        if (!isGsi && total > 100)
        {
            if (settings.ConfirmLargeBatchAsync is null)
            {
                throw new BatchGenerationException("100枚超のカスタム一括生成には、配信元の一括取得・画像保存の許可を確認してから開始してください。");
            }

            bool confirmed;
            try { confirmed = await settings.ConfirmLargeBatchAsync(total, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Summary(cancelled: true); }
            if (!confirmed)
            {
                return Summary(cancelled: true);
            }
        }

        if (Path.IsPathFullyQualified(settings.OutputFolderPath))
        {
            try { Directory.CreateDirectory(settings.OutputFolderPath); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                throw new BatchGenerationException($"出力フォルダを作成できません: {exception.Message}", exception);
            }
        }

        using IDisposable batchLease = this.trafficController.BeginBatch(settings.TileSource);

        for (int index = 0; index < files.Length; index++)
        {
            string filePath = files[index];
            string fileName = Path.GetRelativePath(settings.InputFolderPath, filePath);
            currentIndex = index;
            currentFile = fileName;
            Notify(index, fileName, BatchGenerationStatus.Success, $"生成中: {fileName}", activity: true);
            int displayIndex = index + 1;

            if (cancellationToken.IsCancellationRequested)
            {
                Notify(displayIndex, fileName, BatchGenerationStatus.Cancelled, "処理が手動でキャンセルされました。");
                return Summary(cancelled: true);
            }

            GeoCoordinate? coordinate;
            try
            {
                coordinate = await Task.Run(
                    () => this.exifGpsReader.Read(filePath),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Notify(displayIndex, fileName, BatchGenerationStatus.Cancelled, "処理が手動でキャンセルされました。");
                return Summary(cancelled: true);
            }
            catch (Exception exception) when (exception is ExifGpsReadException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException)
            {
                errorCount++;
                Notify(displayIndex, fileName, BatchGenerationStatus.Error, $"生成エラー: {exception.Message}");
                continue;
            }

            if (coordinate is null)
            {
                skippedCount++;
                Notify(displayIndex, fileName, BatchGenerationStatus.Skip, "GPS情報が見つかりません。");
                continue;
            }

            try
            {
                _ = OutputPathResolver.Resolve(filePath, settings);
                MapCompositionResult composition = await this.mapImageComposer.ComposeAsync(
                    new MapCompositionRequest
                    {
                        Center = coordinate.Value,
                        TileSource = settings.TileSource,
                        Width = settings.Width,
                        Height = settings.Height,
                        Zoom = settings.Zoom,
                        PinImagePath = PhotoMapAssetPaths.ResolvePinImagePath(settings.PinImagePath),
                        AllowWorldwideFallback = false,
                        TileSession = session,
                    },
                    cancellationToken).ConfigureAwait(false);

                string outputPath = OutputPathResolver.Resolve(filePath, settings);
                string outputFileName = Path.GetFileName(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await WriteOutputAsync(outputPath, composition.Png, cancellationToken).ConfigureAwait(false);

                successCount++;
                string message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"位置情報 ({coordinate.Value.Latitude:F5}, {coordinate.Value.Longitude:F5}) -> {outputFileName} を作成しました。");
                Notify(displayIndex, fileName, BatchGenerationStatus.Success, message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Notify(displayIndex, fileName, BatchGenerationStatus.Cancelled, "処理が手動でキャンセルされました。");
                return Summary(cancelled: true);
            }
            catch (TileFetchException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                skippedCount++;
                Notify(displayIndex, fileName, BatchGenerationStatus.Skip, OutsideCoverageMessage);
            }
            catch (Exception exception) when (exception is MapCompositionException
                or TileFetchException
                or ArgumentException
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                errorCount++;
                Notify(displayIndex, fileName, BatchGenerationStatus.Error, $"生成エラー: {exception.Message}");
                if (session.IsStopped)
                {
                    return Summary(cancelled: false, exception.Message);
                }
            }
        }

        return Summary(cancelled: false);
    }

    private static async Task WriteOutputAsync(
        string outputPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        string temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $"{Guid.NewGuid():N}.tmp");
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

}
