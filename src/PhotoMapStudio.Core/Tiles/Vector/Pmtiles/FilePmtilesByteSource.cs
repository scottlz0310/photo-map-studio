using Microsoft.Win32.SafeHandles;

namespace PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

/// <summary>
/// ローカルファイルシステム上の PMTiles アーカイブからバイト範囲を非同期に読み取るバイトソース。
/// </summary>
public sealed class FilePmtilesByteSource : IPmtilesByteSource
{
    private readonly SafeFileHandle fileHandle;
    private bool disposed;

    public FilePmtilesByteSource(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"PMTiles ファイルが見つかりません: {filePath}", filePath);
        }

        this.fileHandle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <inheritdoc/>
    public async Task<byte[]> ReadRangeAsync(long offset, int length, TileFetchSession? session, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        session?.ThrowIfStopped();
        session?.RecordRequest();

        byte[] buffer = new byte[length];
        int totalRead = 0;

        while (totalRead < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await RandomAccess.ReadAsync(
                this.fileHandle,
                buffer.AsMemory(totalRead, length - totalRead),
                offset + totalRead,
                cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException($"PMTiles ファイルの終端に達しました (要求: {length} バイト, 読取: {totalRead} バイト)。");
            }

            totalRead += read;
        }

        session?.RecordSuccess(cached: true);
        return buffer;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.fileHandle.Dispose();
            this.disposed = true;
        }
    }
}
