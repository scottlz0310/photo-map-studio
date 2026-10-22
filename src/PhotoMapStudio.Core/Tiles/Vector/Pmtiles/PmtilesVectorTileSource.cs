using System.Collections.Concurrent;

namespace PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

/// <summary>
/// PMTiles v3 アーカイブからベクタータイルを取得する <see cref="IVectorTileSource"/> 実装。
/// </summary>
public sealed class PmtilesVectorTileSource : IVectorTileSource, IDisposable
{
    private readonly IPmtilesByteSource byteSource;
    private readonly string sourceId;
    private readonly int fallbackMaxZoom;
    private readonly SemaphoreSlim initLock = new(1, 1);
    private readonly ConcurrentDictionary<long, IReadOnlyList<PmtilesEntry>> leafDirectories = new();

    private PmtilesHeader? header;
    private IReadOnlyList<PmtilesEntry>? rootEntries;
    private bool disposed;

    /// <summary>
    /// PMTiles タイルソースを初期化します。
    /// </summary>
    /// <param name="byteSource">PMTiles バイトソース。</param>
    /// <param name="sourceId">ソース識別子。</param>
    /// <param name="fallbackMaxZoom">ヘッダー読み取り前のフォールバック最大ズームレベル (既定: 16)。</param>
    public PmtilesVectorTileSource(
        IPmtilesByteSource byteSource,
        string sourceId,
        int fallbackMaxZoom = 16)
    {
        this.byteSource = byteSource ?? throw new ArgumentNullException(nameof(byteSource));
        this.sourceId = string.IsNullOrWhiteSpace(sourceId) ? "pmtiles" : sourceId;
        this.fallbackMaxZoom = fallbackMaxZoom;
    }

    /// <inheritdoc/>
    public string SourceId => this.sourceId;

    /// <inheritdoc/>
    public int MaxZoom => this.header?.MaxZoom ?? this.fallbackMaxZoom;

    /// <inheritdoc/>
    public async Task<byte[]?> GetTileAsync(
        int z,
        int x,
        int y,
        TileFetchSession? session = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        await this.EnsureInitializedAsync(session, cancellationToken).ConfigureAwait(false);

        PmtilesHeader currentHeader = this.header!;
        IReadOnlyList<PmtilesEntry> currentRoot = this.rootEntries!;

        if (z < currentHeader.MinZoom || z > currentHeader.MaxZoom)
        {
            return null;
        }

        ulong tileId = PmtilesHilbert.ZxyToTileId(z, x, y);

        PmtilesEntry? entry = PmtilesDirectory.FindTile(currentRoot, tileId);

        if (entry != null && entry.IsLeafDirectory)
        {
            IReadOnlyList<PmtilesEntry> leafEntries = await this.GetLeafDirectoryAsync(
                entry.Offset,
                (int)entry.Length,
                currentHeader,
                session,
                cancellationToken).ConfigureAwait(false);

            entry = PmtilesDirectory.FindTile(leafEntries, tileId);
        }

        if (entry == null || entry.IsLeafDirectory)
        {
            // タイルが存在しない（空領域）
            return null;
        }

        long tileOffset = (long)currentHeader.TileDataOffset + entry.Offset;
        byte[] tileBytes = await this.byteSource.ReadRangeAsync(
            tileOffset,
            (int)entry.Length,
            session,
            cancellationToken).ConfigureAwait(false);

        if (currentHeader.TileCompression == PmtilesCompression.Gzip && tileBytes.Length > 0)
        {
            return PmtilesDirectory.Decompress(tileBytes, PmtilesCompression.Gzip);
        }

        return tileBytes;
    }

    private async Task EnsureInitializedAsync(TileFetchSession? session, CancellationToken cancellationToken)
    {
        if (this.header != null && this.rootEntries != null)
        {
            return;
        }

        await this.initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.header != null && this.rootEntries != null)
            {
                return;
            }

            // 1. 先頭 127 バイトのヘッダーを読み取り
            byte[] headerBytes = await this.byteSource.ReadRangeAsync(
                0,
                PmtilesHeader.HeaderSize,
                session,
                cancellationToken).ConfigureAwait(false);

            var parsedHeader = PmtilesHeader.Parse(headerBytes);

            // 2. ルートディレクトリを読み取り・解凍
            byte[] rootBytes = await this.byteSource.ReadRangeAsync(
                (long)parsedHeader.RootDirectoryOffset,
                (int)parsedHeader.RootDirectoryLength,
                session,
                cancellationToken).ConfigureAwait(false);

            var entries = PmtilesDirectory.DecompressAndDecode(rootBytes, parsedHeader.InternalCompression);

            this.header = parsedHeader;
            this.rootEntries = entries;
        }
        finally
        {
            this.initLock.Release();
        }
    }

    private async Task<IReadOnlyList<PmtilesEntry>> GetLeafDirectoryAsync(
        long leafRelativeOffset,
        int leafLength,
        PmtilesHeader currentHeader,
        TileFetchSession? session,
        CancellationToken cancellationToken)
    {
        if (this.leafDirectories.TryGetValue(leafRelativeOffset, out var cached))
        {
            return cached;
        }

        long absoluteOffset = (long)currentHeader.LeafDirectoriesOffset + leafRelativeOffset;
        byte[] leafBytes = await this.byteSource.ReadRangeAsync(
            absoluteOffset,
            leafLength,
            session,
            cancellationToken).ConfigureAwait(false);

        var entries = PmtilesDirectory.DecompressAndDecode(leafBytes, currentHeader.InternalCompression);
        this.leafDirectories.TryAdd(leafRelativeOffset, entries);
        return entries;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.initLock.Dispose();
            this.byteSource.Dispose();
            this.disposed = true;
        }
    }
}
