using System.Buffers.Binary;

namespace PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

/// <summary>
/// PMTiles 内の圧縮アルゴリズムを表します。
/// </summary>
public enum PmtilesCompression
{
    Unknown = 0,
    None = 1,
    Gzip = 2,
    Brotli = 3,
    Zstd = 4,
}

/// <summary>
/// PMTiles のタイル種別を表します。
/// </summary>
public enum PmtilesTileType
{
    Unknown = 0,
    Mvt = 1,
    Png = 2,
    Jpeg = 3,
    Webp = 4,
    Avif = 5,
}

/// <summary>
/// PMTiles v3 の 127 バイト固定ヘッダーを表します。
/// </summary>
public sealed record PmtilesHeader
{
    public const int HeaderSize = 127;
    private static readonly byte[] Magic = "PMTiles"u8.ToArray();

    public byte Version { get; init; } = 3;
    public ulong RootDirectoryOffset { get; init; }
    public ulong RootDirectoryLength { get; init; }
    public ulong JsonMetadataOffset { get; init; }
    public ulong JsonMetadataLength { get; init; }
    public ulong LeafDirectoriesOffset { get; init; }
    public ulong LeafDirectoriesLength { get; init; }
    public ulong TileDataOffset { get; init; }
    public ulong TileDataLength { get; init; }
    public ulong AddressedTilesCount { get; init; }
    public ulong TileEntriesCount { get; init; }
    public ulong TileContentsCount { get; init; }
    public bool Clustered { get; init; }
    public PmtilesCompression InternalCompression { get; init; }
    public PmtilesCompression TileCompression { get; init; }
    public PmtilesTileType TileType { get; init; }
    public byte MinZoom { get; init; }
    public byte MaxZoom { get; init; }
    public double MinLon { get; init; }
    public double MinLat { get; init; }
    public double MaxLon { get; init; }
    public double MaxLat { get; init; }
    public byte CenterZoom { get; init; }
    public double CenterLon { get; init; }
    public double CenterLat { get; init; }

    /// <summary>
    /// 127 バイトのヘッダーバイナリを解析します。
    /// </summary>
    public static PmtilesHeader Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
        {
            throw new FormatException($"PMTiles ヘッダーサイズが不正です (必要: {HeaderSize} バイト, 実際: {buffer.Length} バイト)。");
        }

        if (!buffer[..7].SequenceEqual(Magic))
        {
            throw new FormatException("PMTiles マジックナンバーが一致しません。");
        }

        byte version = buffer[7];
        if (version != 3)
        {
            throw new NotSupportedException($"未対応の PMTiles バージョンです: {version} (v3 のみサポート)");
        }

        return new PmtilesHeader
        {
            Version = version,
            RootDirectoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..16]),
            RootDirectoryLength = BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..24]),
            JsonMetadataOffset = BinaryPrimitives.ReadUInt64LittleEndian(buffer[24..32]),
            JsonMetadataLength = BinaryPrimitives.ReadUInt64LittleEndian(buffer[32..40]),
            LeafDirectoriesOffset = BinaryPrimitives.ReadUInt64LittleEndian(buffer[40..48]),
            LeafDirectoriesLength = BinaryPrimitives.ReadUInt64LittleEndian(buffer[48..56]),
            TileDataOffset = BinaryPrimitives.ReadUInt64LittleEndian(buffer[56..64]),
            TileDataLength = BinaryPrimitives.ReadUInt64LittleEndian(buffer[64..72]),
            AddressedTilesCount = BinaryPrimitives.ReadUInt64LittleEndian(buffer[72..80]),
            TileEntriesCount = BinaryPrimitives.ReadUInt64LittleEndian(buffer[80..88]),
            TileContentsCount = BinaryPrimitives.ReadUInt64LittleEndian(buffer[88..96]),
            Clustered = buffer[96] != 0,
            InternalCompression = (PmtilesCompression)buffer[97],
            TileCompression = (PmtilesCompression)buffer[98],
            TileType = (PmtilesTileType)buffer[99],
            MinZoom = buffer[100],
            MaxZoom = buffer[101],
            MinLon = BinaryPrimitives.ReadInt32LittleEndian(buffer[102..106]) / 10000000.0,
            MinLat = BinaryPrimitives.ReadInt32LittleEndian(buffer[106..110]) / 10000000.0,
            MaxLon = BinaryPrimitives.ReadInt32LittleEndian(buffer[110..114]) / 10000000.0,
            MaxLat = BinaryPrimitives.ReadInt32LittleEndian(buffer[114..118]) / 10000000.0,
            CenterZoom = buffer[118],
            CenterLon = BinaryPrimitives.ReadInt32LittleEndian(buffer[119..123]) / 10000000.0,
            CenterLat = BinaryPrimitives.ReadInt32LittleEndian(buffer[123..127]) / 10000000.0,
        };
    }
}

/// <summary>
/// PMTiles のディレクトリ内の 1 エントリを表します。
/// </summary>
public sealed class PmtilesEntry
{
    public ulong TileId { get; set; }

    public long Offset { get; set; }

    public uint Length { get; set; }

    /// <summary>
    /// 0 の場合はリーフディレクトリの参照、1 以上の場合は該当タイルおよび同一内容タイルの連長。
    /// </summary>
    public uint RunLength { get; set; }

    /// <summary>
    /// リーフディレクトリ参照かどうか。
    /// </summary>
    public bool IsLeafDirectory => this.RunLength == 0;
}
