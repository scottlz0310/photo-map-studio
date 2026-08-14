namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// ファイルシステム上のタイルキャッシュ。
/// </summary>
public sealed class FileSystemTileCache : ITileCache
{
    /// <summary>OSM Tile Usage Policy が求める保持期間の下限。</summary>
    public static TimeSpan MinimumRetention { get; } = TimeSpan.FromDays(7);

    /// <summary>既定の保持期間。</summary>
    public static TimeSpan DefaultRetention { get; } = TimeSpan.FromDays(30);

    private readonly string rootPath;
    private readonly TimeSpan retention;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// キャッシュを初期化する。
    /// </summary>
    /// <param name="rootPath">キャッシュディレクトリ。</param>
    /// <param name="retention">保持期間。<see cref="MinimumRetention"/> を下回る値は指定できない。</param>
    /// <param name="timeProvider">期限判定に使う時刻源。</param>
    public FileSystemTileCache(string rootPath, TimeSpan? retention = null, TimeProvider? timeProvider = null)
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
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
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

        // 途中で失敗した内容を読み出さないよう、一時ファイルへ書いてから置き換える
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private string ResolvePath(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // キャッシュディレクトリ外を指すキーを弾く
        if (!string.Equals(Path.GetFileName(key), key, StringComparison.Ordinal))
        {
            throw new ArgumentException($"キャッシュキーにパス区切りを含めることはできません: {key}", nameof(key));
        }

        return Path.Combine(this.rootPath, key);
    }
}
