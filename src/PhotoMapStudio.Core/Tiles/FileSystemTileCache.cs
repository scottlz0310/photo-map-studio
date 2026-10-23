namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// ファイルシステム上のタイルキャッシュ。
/// </summary>
public sealed class FileSystemTileCache : ITileCache, IDisposable
{
    /// <summary>OSM Tile Usage Policy が求める保持期間の下限。</summary>
    public static TimeSpan MinimumRetention { get; } = TimeSpan.FromDays(7);

    /// <summary>既定の保持期間。</summary>
    public static TimeSpan DefaultRetention { get; } = TimeSpan.FromDays(30);

    /// <summary>既定のキャッシュ容量上限（1GB = 1024MB）。</summary>
    public const long DefaultMaxSizeBytes = 1024L * 1024L * 1024L;

    private readonly string rootPath;
    private readonly TimeSpan retention;
    private readonly TimeProvider timeProvider;
    private readonly Action<Exception>? errorObserver;
    private readonly SemaphoreSlim gate = new(1, 1);
    private long maxSizeBytes;

    // 容量判定のたびにディレクトリ全体を列挙しないよう、合計サイズを保持する（-1 は未確定）。
    // 書き込み・整理・消去は gate の中で更新する。外部からの変更は次の整理（全列挙）で補正される。
    private long trackedSizeBytes = -1;

    /// <summary>
    /// キャッシュ容量上限（バイト数）。0 以下の場合は無制限。
    /// </summary>
    /// <remarks>
    /// 代入では整理しない（UI スレッドで大量のファイルを列挙・削除しないため）。
    /// 上限を下げた場合の整理は、次の書き込み時、または <see cref="PruneAsync"/> で行う。
    /// </remarks>
    public long MaxSizeBytes
    {
        get => Interlocked.Read(ref this.maxSizeBytes);
        set => Interlocked.Exchange(ref this.maxSizeBytes, value);
    }

    /// <summary>
    /// キャッシュを初期化する。
    /// </summary>
    /// <param name="rootPath">キャッシュディレクトリ。</param>
    /// <param name="retention">保持期間。<see cref="MinimumRetention"/> を下回る値は指定できない。</param>
    /// <param name="timeProvider">期限判定に使う時刻源。</param>
    /// <param name="maxSizeBytes">キャッシュ容量上限（バイト数）。未指定時は <see cref="DefaultMaxSizeBytes"/>（1GB）。</param>
    /// <param name="errorObserver">
    /// 読み書き・整理の途中で起きた IO エラー（キャッシュは無視して処理を続けるもの）の通知先。例外を送出しないこと。
    /// 整理・消去では 1 回の操作につき最初の 1 件だけ通知する。
    /// </param>
    public FileSystemTileCache(
        string rootPath,
        TimeSpan? retention = null,
        TimeProvider? timeProvider = null,
        long? maxSizeBytes = null,
        Action<Exception>? errorObserver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        TimeSpan effectiveRetention = retention ?? DefaultRetention;
        if (effectiveRetention < MinimumRetention)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retention),
                effectiveRetention,
                $"保持期間は {MinimumRetention.TotalDays} 日以上である必要があります。");
        }

        this.rootPath = rootPath;
        this.retention = effectiveRetention;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.maxSizeBytes = maxSizeBytes ?? DefaultMaxSizeBytes;
        this.errorObserver = errorObserver;
    }

    /// <inheritdoc />
    public async Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
    {
        string path = this.ResolvePath(key);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        if (this.timeProvider.GetUtcNow() - file.LastWriteTimeUtc > this.retention)
        {
            return null;
        }

        try
        {
            byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

            // LRU 用にアクセス日時を更新する
            try
            {
                File.SetLastAccessTimeUtc(path, this.timeProvider.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // アクセス日時を更新できなくても内容は返す（LRU の精度が落ちるだけ）
                this.errorObserver?.Invoke(ex);
            }

            return content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 読み取りに失敗した場合はキャッシュを無視してネットワーク取得へ進む（仕様書 §5.3-2）
            this.errorObserver?.Invoke(ex);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        string path = this.ResolvePath(key);
        Directory.CreateDirectory(this.rootPath);

        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            this.EnsureTrackedSize();

            long replaced = new FileInfo(path) is { Exists: true } existing ? existing.Length : 0;
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
            Interlocked.Add(ref this.trackedSizeBytes, content.Length - replaced);

            this.EnsureCapacity(cancellationToken);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 元の例外を優先して再送出する。残った一時ファイルは次回の初期化走査で回収される
                    this.errorObserver?.Invoke(ex);
                }
            }
            throw;
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc />
    public long GetCurrentSizeBytes()
    {
        long tracked = Interlocked.Read(ref this.trackedSizeBytes);
        if (tracked >= 0)
        {
            return tracked;
        }

        // 未確定（まだ書き込み・整理をしていない）ときだけ列挙する
        return Directory.Exists(this.rootPath) ? this.ScanSizeBytes(reclaimTemporaryFiles: false) : 0;
    }

    /// <inheritdoc />
    public async Task<bool> ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(this.rootPath))
        {
            return true;
        }

        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 大量のファイルを呼び出し元のスレッド（UI）で削除しない
            return await Task.Run(() => this.ClearCore(cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task PruneAsync(long targetSizeBytes, CancellationToken cancellationToken = default)
    {
        if (targetSizeBytes < 0)
        {
            targetSizeBytes = 0;
        }

        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 大量のファイルを呼び出し元のスレッド（UI）で列挙・削除しない
            await Task.Run(() => this.PruneCore(targetSizeBytes, cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private bool ClearCore(CancellationToken cancellationToken)
    {
        bool allSuccess = true;
        bool reported = false;
        try
        {
            var dir = new DirectoryInfo(this.rootPath);
            foreach (FileInfo file in dir.EnumerateFiles())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref this.trackedSizeBytes, -1);
                    return false;
                }

                try
                {
                    if (file.IsReadOnly)
                    {
                        file.Attributes = FileAttributes.Normal;
                    }
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    allSuccess = false;
                    if (!reported)
                    {
                        reported = true;
                        this.errorObserver?.Invoke(ex);
                    }
                }
            }

            // 削除後に未削除ファイルが残っているか再確認
            if (dir.EnumerateFiles().Any())
            {
                allSuccess = false;
            }

            // 残りがあるときは次の走査で確定させる
            Interlocked.Exchange(ref this.trackedSizeBytes, allSuccess ? 0 : -1);
            return allSuccess;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.errorObserver?.Invoke(ex);
            Interlocked.Exchange(ref this.trackedSizeBytes, -1);
            return false;
        }
    }

    /// <summary>合計サイズが未確定なら一度だけ走査して確定する。gate の中で呼ぶ。</summary>
    private void EnsureTrackedSize()
    {
        if (Interlocked.Read(ref this.trackedSizeBytes) >= 0 || !Directory.Exists(this.rootPath))
        {
            return;
        }

        // gate の中で最初に呼ばれるため、この時点で残っている .tmp は前回の異常終了の残骸
        Interlocked.Exchange(ref this.trackedSizeBytes, this.ScanSizeBytes(reclaimTemporaryFiles: true));
    }

    private long ScanSizeBytes(bool reclaimTemporaryFiles)
    {
        long total = 0;
        bool reported = false;
        try
        {
            foreach (FileInfo file in new DirectoryInfo(this.rootPath).EnumerateFiles())
            {
                if (!file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    total += file.Length;
                }
                else if (reclaimTemporaryFiles)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (!reported)
                        {
                            reported = true;
                            this.errorObserver?.Invoke(ex);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.errorObserver?.Invoke(ex);
        }

        return total;
    }

    private void EnsureCapacity(CancellationToken cancellationToken)
    {
        long limit = this.MaxSizeBytes;
        if (limit <= 0 || Interlocked.Read(ref this.trackedSizeBytes) <= limit)
        {
            return;
        }

        // 上限超過時は、90% を安全目標サイズとして古い順に削除する
        this.PruneCore((long)(limit * 0.90), cancellationToken);
    }

    private void PruneCore(long targetSizeBytes, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(this.rootPath))
        {
            Interlocked.Exchange(ref this.trackedSizeBytes, 0);
            return;
        }

        try
        {
            var dir = new DirectoryInfo(this.rootPath);
            FileInfo[] files = dir.GetFiles();
            long totalSize = 0;
            var candidateFiles = new List<FileInfo>(files.Length);
            foreach (FileInfo file in files)
            {
                if (!file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    totalSize += file.Length;
                    candidateFiles.Add(file);
                }
            }

            if (totalSize > targetSizeBytes)
            {
                // LRU 順: LastAccessTimeUtc と LastWriteTimeUtc の新しい方を最終使用日時とし、昇順（古い順）にソート
                candidateFiles.Sort(static (a, b) =>
                {
                    DateTime ta = a.LastAccessTimeUtc > a.LastWriteTimeUtc ? a.LastAccessTimeUtc : a.LastWriteTimeUtc;
                    DateTime tb = b.LastAccessTimeUtc > b.LastWriteTimeUtc ? b.LastAccessTimeUtc : b.LastWriteTimeUtc;
                    return ta.CompareTo(tb);
                });

                bool reported = false;
                foreach (FileInfo file in candidateFiles)
                {
                    if (cancellationToken.IsCancellationRequested || totalSize <= targetSizeBytes)
                    {
                        break;
                    }

                    try
                    {
                        long length = file.Length;
                        if (file.IsReadOnly)
                        {
                            file.Attributes = FileAttributes.Normal;
                        }
                        file.Delete();
                        totalSize -= length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 使用中などで消せないファイルは飛ばして次の候補へ進む（容量は削除できた分だけ減る）
                        if (!reported)
                        {
                            reported = true;
                            this.errorObserver?.Invoke(ex);
                        }
                    }
                }
            }

            // 走査した実測値で合計サイズを確定する（外部からの変更もここで補正される）
            Interlocked.Exchange(ref this.trackedSizeBytes, totalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.errorObserver?.Invoke(ex);
            Interlocked.Exchange(ref this.trackedSizeBytes, -1);
        }
    }

    private string ResolvePath(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // キャッシュディレクトリ外を指すキーを弾く
        if (!string.Equals(Path.GetFileName(key), key, StringComparison.Ordinal))
        {
            throw new ArgumentException("キャッシュキーに無効な文字が含まれています。", nameof(key));
        }

        return Path.Combine(this.rootPath, key);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.gate.Dispose();
    }
}
