using System.Buffers.Binary;

namespace PhotoMapStudio.Tiles.Vector.Pmtiles;

/// <summary>
/// PMTiles 内の圧縮アルゴリズムを表します。
/// </summary>
public enum PmtilesCompression
{
    /// <summary>不明。</summary>
    Unknown = 0,

    /// <summary>圧縮なし。</summary>
    None = 1,

    /// <summary>gzip。</summary>
    Gzip = 2,

    /// <summary>Brotli。</summary>
    Brotli = 3,

    /// <summary>Zstandard。</summary>
    Zstd = 4,
}

/// <summary>
/// PMTiles のタイル種別を表します。
/// </summary>
public enum PmtilesTileType
{
    /// <summary>不明。</summary>
    Unknown = 0,

    /// <summary>Mapbox Vector Tile（ベクタータイル）。</summary>
    Mvt = 1,

    /// <summary>PNG 画像。</summary>
    Png = 2,

    /// <summary>JPEG 画像。</summary>
    Jpeg = 3,

    /// <summary>WebP 画像。</summary>
    Webp = 4,

    /// <summary>AVIF 画像。</summary>
    Avif = 5,
}

/// <summary>
/// PMTiles v3 の 127 バイト固定ヘッダーを表します。
/// </summary>
public sealed record PmtilesHeader
{
    /// <summary>ヘッダーのバイト数。</summary>
    public const int HeaderSize = 127;

    private static readonly byte[] Magic = "PMTiles"u8.ToArray();

    /// <summary>PMTiles 仕様のバージョン（v3 のみ対応）。</summary>
    public byte Version { get; init; } = 3;

    /// <summary>ルートディレクトリのファイル先頭からのバイト位置。</summary>
    public ulong RootDirectoryOffset { get; init; }

    /// <summary>ルートディレクトリのバイト長。</summary>
    public ulong RootDirectoryLength { get; init; }

    /// <summary>JSON メタデータのファイル先頭からのバイト位置。</summary>
    public ulong JsonMetadataOffset { get; init; }

    /// <summary>JSON メタデータのバイト長。</summary>
    public ulong JsonMetadataLength { get; init; }

    /// <summary>リーフディレクトリ領域のファイル先頭からのバイト位置。</summary>
    public ulong LeafDirectoriesOffset { get; init; }

    /// <summary>リーフディレクトリ領域のバイト長。</summary>
    public ulong LeafDirectoriesLength { get; init; }

    /// <summary>タイルデータ領域のファイル先頭からのバイト位置。</summary>
    public ulong TileDataOffset { get; init; }

    /// <summary>タイルデータ領域のバイト長。</summary>
    public ulong TileDataLength { get; init; }

    /// <summary>アーカイブ内でアドレスできるタイルの数（0 は不明）。</summary>
    public ulong AddressedTilesCount { get; init; }

    /// <summary>ディレクトリ内のタイルエントリの数（0 は不明）。</summary>
    public ulong TileEntriesCount { get; init; }

    /// <summary>重複を除いたタイルの内容の数（0 は不明）。</summary>
    public ulong TileContentsCount { get; init; }

    /// <summary>タイルデータが、タイル ID の順に並んでいるか。</summary>
    public bool Clustered { get; init; }

    /// <summary>ディレクトリと JSON メタデータの圧縮。</summary>
    public PmtilesCompression InternalCompression { get; init; }

    /// <summary>タイルデータの圧縮。</summary>
    public PmtilesCompression TileCompression { get; init; }

    /// <summary>タイルの種別。</summary>
    public PmtilesTileType TileType { get; init; }

    /// <summary>収録している最小のズームレベル。</summary>
    public byte MinZoom { get; init; }

    /// <summary>収録している最大のズームレベル。</summary>
    public byte MaxZoom { get; init; }

    /// <summary>収録範囲の西端の経度（度）。</summary>
    public double MinLon { get; init; }

    /// <summary>収録範囲の南端の緯度（度）。</summary>
    public double MinLat { get; init; }

    /// <summary>収録範囲の東端の経度（度）。</summary>
    public double MaxLon { get; init; }

    /// <summary>収録範囲の北端の緯度（度）。</summary>
    public double MaxLat { get; init; }

    /// <summary>表示の中心とするズームレベル。</summary>
    public byte CenterZoom { get; init; }

    /// <summary>表示の中心の経度（度）。</summary>
    public double CenterLon { get; init; }

    /// <summary>表示の中心の緯度（度）。</summary>
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
    /// <summary>Hilbert 曲線上のタイル ID（先頭のタイル、または連長の開始）。</summary>
    public ulong TileId { get; set; }

    /// <summary>タイルデータ領域（リーフディレクトリ参照のときはリーフディレクトリ領域）の先頭からのバイト位置。</summary>
    public long Offset { get; set; }

    /// <summary>タイルまたはリーフディレクトリのバイト長。</summary>
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
