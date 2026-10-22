using System.Buffers.Binary;
using System.IO.Compression;

using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector.Pmtiles;

public sealed class PmtilesVectorTileSourceTests
{
    private sealed class MemoryPmtilesByteSource : IPmtilesByteSource
    {
        private readonly byte[] buffer;
        public int ReadCount { get; private set; }

        public MemoryPmtilesByteSource(byte[] buffer)
        {
            this.buffer = buffer;
        }

        public Task<byte[]> ReadRangeAsync(long offset, int length, TileFetchSession? session, CancellationToken cancellationToken)
        {
            this.ReadCount++;
            session?.ThrowIfStopped();
            session?.RecordRequest();

            if (offset + length > this.buffer.Length)
            {
                var ex = new TileFetchException($"要求範囲がバッファの長さを超えています (offset: {offset}, length: {length})");
                session?.RecordFailure(ex);
                throw ex;
            }

            byte[] result = new byte[length];
            Array.Copy(this.buffer, offset, result, 0, length);
            session?.RecordSuccess(cached: false);
            return Task.FromResult(result);
        }

        public void Dispose()
        {
        }
    }

    private static byte[] BuildTestPmtiles(
        IReadOnlyList<PmtilesEntry> rootEntries,
        IReadOnlyList<PmtilesEntry>? leafEntries,
        byte[] tileData,
        bool gzipTiles = true)
    {
        using var ms = new MemoryStream();

        // 127 bytes header placeholder
        byte[] headerBytes = new byte[PmtilesHeader.HeaderSize];
        "PMTiles"u8.CopyTo(headerBytes.AsSpan(0, 7));
        headerBytes[7] = 3; // version 3
        ms.Write(headerBytes);

        // Root directory
        byte[] rootEncoded = PmtilesDirectory.Encode(rootEntries);
        long rootOffset = ms.Position;
        ms.Write(rootEncoded);
        long rootLength = rootEncoded.Length;

        // Leaf directory (optional)
        long leafOffset = ms.Position;
        long leafLength = 0;
        if (leafEntries != null)
        {
            byte[] leafEncoded = PmtilesDirectory.Encode(leafEntries);
            ms.Write(leafEncoded);
            leafLength = leafEncoded.Length;
        }

        // Tile data
        long tileDataOffset = ms.Position;
        byte[] finalTileData = tileData;
        if (gzipTiles)
        {
            using var tileMs = new MemoryStream();
            using (var gzip = new GZipStream(tileMs, CompressionLevel.Optimal))
            {
                gzip.Write(tileData);
            }

            finalTileData = tileMs.ToArray();
        }

        ms.Write(finalTileData);
        long tileDataLength = finalTileData.Length;

        // Fill header offsets
        BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(8, 8), (ulong)rootOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(16, 8), (ulong)rootLength);
        BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(40, 8), (ulong)leafOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(48, 8), (ulong)leafLength);
        BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(56, 8), (ulong)tileDataOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(64, 8), (ulong)tileDataLength);
        headerBytes[97] = (byte)PmtilesCompression.None; // InternalCompression
        headerBytes[98] = (byte)(gzipTiles ? PmtilesCompression.Gzip : PmtilesCompression.None); // TileCompression
        headerBytes[100] = 0; // MinZoom
        headerBytes[101] = 16; // MaxZoom

        // Rewrite header at beginning
        byte[] fullBytes = ms.ToArray();
        Array.Copy(headerBytes, 0, fullBytes, 0, PmtilesHeader.HeaderSize);
        return fullBytes;
    }

    [Fact]
    public async Task ルートディレクトリから直接タイルを取得してGzip解凍できる()
    {
        byte[] rawTile = [0x1A, 0x2B, 0x3C, 0x4D];
        ulong targetTileId = PmtilesHilbert.ZxyToTileId(14, 14552, 6451);

        var rootEntries = new List<PmtilesEntry>
        {
            new() { TileId = targetTileId, Offset = 0, Length = 0, RunLength = 1 }, // Length will be adjusted
        };

        // Gzip compression test
        using var gzipMs = new MemoryStream();
        using (var gzip = new GZipStream(gzipMs, CompressionLevel.Optimal, true))
        {
            gzip.Write(rawTile);
        }

        byte[] compressedTile = gzipMs.ToArray();
        rootEntries[0].Length = (uint)compressedTile.Length;

        byte[] pmtiles = BuildTestPmtiles(rootEntries, null, rawTile, gzipTiles: true);
        using var byteSource = new MemoryPmtilesByteSource(pmtiles);
        using var tileSource = new PmtilesVectorTileSource(byteSource, "test-gsi");

        byte[]? fetched = await tileSource.GetTileAsync(14, 14552, 6451, cancellationToken: CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(rawTile, fetched);
        Assert.Equal("test-gsi", tileSource.SourceId);
        Assert.Equal(16, tileSource.MaxZoom);
    }

    [Fact]
    public async Task リーフディレクトリを経由してタイルを取得できる()
    {
        byte[] rawTile = [0xAA, 0xBB, 0xCC];
        ulong targetTileId = PmtilesHilbert.ZxyToTileId(16, 58208, 25804);

        // ルートにはリーフへの参照 (runLength = 0)
        var rootEntries = new List<PmtilesEntry>
        {
            new() { TileId = targetTileId - 10, Offset = 0, Length = 0, RunLength = 0 }, // Leaf
        };

        var leafEntries = new List<PmtilesEntry>
        {
            new() { TileId = targetTileId, Offset = 0, Length = (uint)rawTile.Length, RunLength = 1 },
        };

        byte[] leafEncoded = PmtilesDirectory.Encode(leafEntries);
        rootEntries[0].Length = (uint)leafEncoded.Length;

        byte[] pmtiles = BuildTestPmtiles(rootEntries, leafEntries, rawTile, gzipTiles: false);
        using var byteSource = new MemoryPmtilesByteSource(pmtiles);
        using var tileSource = new PmtilesVectorTileSource(byteSource, "test-leaf");

        byte[]? fetched = await tileSource.GetTileAsync(16, 58208, 25804, cancellationToken: CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(rawTile, fetched);
    }

    [Fact]
    public async Task 存在しないタイル座標の場合はnullを返す()
    {
        byte[] rawTile = [0x01];
        var rootEntries = new List<PmtilesEntry>
        {
            new() { TileId = 100, Offset = 0, Length = 1, RunLength = 1 },
        };

        byte[] pmtiles = BuildTestPmtiles(rootEntries, null, rawTile, gzipTiles: false);
        using var byteSource = new MemoryPmtilesByteSource(pmtiles);
        using var tileSource = new PmtilesVectorTileSource(byteSource, "test-empty");

        // 存在しないタイル (z=14, x=0, y=0)
        byte[]? fetched = await tileSource.GetTileAsync(14, 0, 0, cancellationToken: CancellationToken.None);

        Assert.Null(fetched);
    }

    [Fact]
    public async Task セッション情報がバイトソースに伝播して記録される()
    {
        byte[] rawTile = [0x11, 0x22];
        ulong targetTileId = PmtilesHilbert.ZxyToTileId(14, 100, 200);
        var rootEntries = new List<PmtilesEntry>
        {
            new() { TileId = targetTileId, Offset = 0, Length = 2, RunLength = 1 },
        };

        byte[] pmtiles = BuildTestPmtiles(rootEntries, null, rawTile, gzipTiles: false);
        using var byteSource = new MemoryPmtilesByteSource(pmtiles);
        using var tileSource = new PmtilesVectorTileSource(byteSource, "test-session");
        var session = new TileFetchSession(TimeProvider.System);

        byte[]? fetched = await tileSource.GetTileAsync(14, 100, 200, session, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.True(session.Snapshot().NetworkCount > 0);
        Assert.False(session.IsStopped);
    }
}
