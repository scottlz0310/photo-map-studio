using System.IO.Compression;

namespace PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

/// <summary>
/// PMTiles v3 のディレクトリのデコードおよびタイル探索を提供します。
/// </summary>
public static class PmtilesDirectory
{
    /// <summary>
    /// 圧縮されたディレクトリバイナリを解凍し、エントリ一覧をデコードします。
    /// </summary>
    public static IReadOnlyList<PmtilesEntry> DecompressAndDecode(ReadOnlySpan<byte> compressedData, PmtilesCompression compression)
    {
        byte[] decompressed = Decompress(compressedData, compression);
        return Decode(decompressed);
    }

    /// <summary>
    /// 解凍済みのディレクトリバイナリからエントリ一覧をデコードします。
    /// </summary>
    public static IReadOnlyList<PmtilesEntry> Decode(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return Array.Empty<PmtilesEntry>();
        }

        int offset = 0;
        ulong numEntriesUlong = PmtilesVarint.ReadVarint(buffer, ref offset);
        if (numEntriesUlong > int.MaxValue)
        {
            throw new FormatException("ディレクトリ内のエントリ数が上限を超えています。");
        }

        int numEntries = (int)numEntriesUlong;
        var entries = new PmtilesEntry[numEntries];

        // 1. Tile IDs (累積デルタ加算)
        ulong lastId = 0;
        for (int i = 0; i < numEntries; i++)
        {
            ulong delta = PmtilesVarint.ReadVarint(buffer, ref offset);
            lastId += delta;
            entries[i] = new PmtilesEntry { TileId = lastId };
        }

        // 2. RunLengths
        for (int i = 0; i < numEntries; i++)
        {
            entries[i].RunLength = (uint)PmtilesVarint.ReadVarint(buffer, ref offset);
        }

        // 3. Lengths
        for (int i = 0; i < numEntries; i++)
        {
            entries[i].Length = (uint)PmtilesVarint.ReadVarint(buffer, ref offset);
        }

        // 4. Offsets
        long nextByte = 0;
        for (int i = 0; i < numEntries; i++)
        {
            ulong o = PmtilesVarint.ReadVarint(buffer, ref offset);
            if (o == 0)
            {
                entries[i].Offset = nextByte;
            }
            else
            {
                entries[i].Offset = (long)(o - 1);
            }

            nextByte = entries[i].Offset + entries[i].Length;
        }

        return entries;
    }

    /// <summary>
    /// エントリ一覧を PMTiles v3 ディレクトリバイナリにエンコードします。
    /// </summary>
    public static byte[] Encode(IReadOnlyList<PmtilesEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        using var ms = new MemoryStream();
        PmtilesVarint.WriteVarint((ulong)entries.Count, ms);

        // 1. Tile IDs (デルタ)
        ulong lastId = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ulong delta = entries[i].TileId - lastId;
            PmtilesVarint.WriteVarint(delta, ms);
            lastId = entries[i].TileId;
        }

        // 2. RunLengths
        for (int i = 0; i < entries.Count; i++)
        {
            PmtilesVarint.WriteVarint(entries[i].RunLength, ms);
        }

        // 3. Lengths
        for (int i = 0; i < entries.Count; i++)
        {
            PmtilesVarint.WriteVarint(entries[i].Length, ms);
        }

        // 4. Offsets
        long nextByte = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            long o = entries[i].Offset;
            if (o == nextByte)
            {
                PmtilesVarint.WriteVarint(0, ms);
            }
            else
            {
                PmtilesVarint.WriteVarint((ulong)(o + 1), ms);
            }

            nextByte = entries[i].Offset + entries[i].Length;
        }

        return ms.ToArray();
    }

    /// <summary>
    /// ソートされたエントリ一覧から、対象の Tile ID に該当するエントリまたはリーフディレクトリを二分探索します。
    /// </summary>
    public static PmtilesEntry? FindTile(IReadOnlyList<PmtilesEntry> entries, ulong tileId)
    {
        if (entries == null || entries.Count == 0)
        {
            return null;
        }

        int m = 0;
        int n = entries.Count - 1;
        while (m <= n)
        {
            int k = (m + n) >> 1;
            long cmp = (long)tileId - (long)entries[k].TileId;
            if (cmp > 0)
            {
                m = k + 1;
            }
            else if (cmp < 0)
            {
                n = k - 1;
            }
            else
            {
                return entries[k];
            }
        }

        if (n >= 0)
        {
            if (entries[n].RunLength == 0 || entries[n].TileId + entries[n].RunLength > tileId)
            {
                return entries[n];
            }
        }

        return null;
    }

    /// <summary>
    /// 指定された圧縮形式でデータを解凍します。
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> compressedData, PmtilesCompression compression)
    {
        switch (compression)
        {
            case PmtilesCompression.None:
                return compressedData.ToArray();

            case PmtilesCompression.Gzip:
                using (var inputMs = new MemoryStream(compressedData.ToArray()))
                using (var gzip = new GZipStream(inputMs, CompressionMode.Decompress))
                using (var outputMs = new MemoryStream())
                {
                    gzip.CopyTo(outputMs);
                    return outputMs.ToArray();
                }

            case PmtilesCompression.Brotli:
                using (var inputMs = new MemoryStream(compressedData.ToArray()))
                using (var brotli = new BrotliStream(inputMs, CompressionMode.Decompress))
                using (var outputMs = new MemoryStream())
                {
                    brotli.CopyTo(outputMs);
                    return outputMs.ToArray();
                }

            default:
                throw new NotSupportedException($"未対応の圧縮形式です: {compression}");
        }
    }
}
