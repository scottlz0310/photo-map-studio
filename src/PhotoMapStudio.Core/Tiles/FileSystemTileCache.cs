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
    private readonly SemaphoreSlim gate = new(1, 1);
    private long maxSizeBytes;

    /// <summary>
    /// キャッシュ容量上限（バイト数）。0 以下の場合は無制限。
    /// </summary>
    public long MaxSizeBytes
    {
        get => this.maxSizeBytes;
        set
        {
            this.maxSizeBytes = value;
            if (value > 0)
            {
                this.PruneCoreSynchronized(value, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// キャッシュを初期化する。
    /// </summary>
    /// <param name="rootPath">キャッシュディレクトリ。</param>
    /// <param name="retention">保持期間。<see cref="MinimumRetention"/> を下回る値は指定できない。</param>
    /// <param name="timeProvider">期限判定に使う時刻源。</param>
    /// <param name="maxSizeBytes">キャッシュ容量上限（バイト数）。未指定時は <see cref="DefaultMaxSizeBytes"/>（1GB）。</param>
    public FileSystemTileCache(
        string rootPath,
        TimeSpan? retention = null,
        TimeProvider? timeProvider = null,
        long? maxSizeBytes = null)
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
            }

            return content;
        }
        catch (IOException)
        {
            // 読み取りに失敗した場合はキャッシュを無視してネットワーク取得へ進む（仕様書 §5.3-2）
            return null;
        }
        catch (UnauthorizedAccessException)
        {
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
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
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
        if (!Directory.Exists(this.rootPath))
        {
            return 0;
        }

        try
        {
            long total = 0;
            foreach (FileInfo file in new DirectoryInfo(this.rootPath).EnumerateFiles())
            {
                if (!file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    total += file.Length;
                }
            }

            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
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
            bool allSuccess = true;
            var dir = new DirectoryInfo(this.rootPath);
            foreach (FileInfo file in dir.EnumerateFiles())
            {
                if (cancellationToken.IsCancellationRequested)
                {
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
                }
            }

            // 削除後に未削除ファイルが残っているか再確認
            if (dir.EnumerateFiles().Any())
            {
                allSuccess = false;
            }

            return allSuccess;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
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
            this.PruneCore(targetSizeBytes, cancellationToken);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private void PruneCoreSynchronized(long targetSizeBytes, CancellationToken cancellationToken)
    {
        this.gate.Wait(cancellationToken);
        try
        {
            this.PruneCore(targetSizeBytes, cancellationToken);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private void EnsureCapacity(CancellationToken cancellationToken)
    {
        if (this.MaxSizeBytes <= 0 || !Directory.Exists(this.rootPath))
        {
            return;
        }

        long currentSize = this.GetCurrentSizeBytes();
        if (currentSize <= this.MaxSizeBytes)
        {
            return;
        }

        // 上限超過時は、90% を安全目標サイズとして古い順に削除する
        long targetSize = (long)(this.MaxSizeBytes * 0.90);
        this.PruneCore(targetSize, cancellationToken);
    }

    private void PruneCore(long targetSizeBytes, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(this.rootPath))
        {
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

            if (totalSize <= targetSizeBytes)
            {
                return;
            }

            // LRU 順: LastAccessTimeUtc と LastWriteTimeUtc の新しい方を最終使用日時とし、昇順（古い順）にソート
            candidateFiles.Sort(static (a, b) =>
            {
                DateTime ta = a.LastAccessTimeUtc > a.LastWriteTimeUtc ? a.LastAccessTimeUtc : a.LastWriteTimeUtc;
                DateTime tb = b.LastAccessTimeUtc > b.LastWriteTimeUtc ? b.LastAccessTimeUtc : b.LastWriteTimeUtc;
                return ta.CompareTo(tb);
            });

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
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
