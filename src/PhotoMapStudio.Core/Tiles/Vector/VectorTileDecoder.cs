using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// Mapbox Vector Tile (MVT / PBF) バイナリデータをデコードする静的パーサー。
/// </summary>
public static class VectorTileDecoder
{
    private static readonly byte[] GzipMagic = [0x1F, 0x8B];

    /// <summary>
    /// MVT バイト列（未圧縮または GZip 圧縮）をデコードして <see cref="VectorTile"/> を生成する。
    /// </summary>
    /// <param name="data">ベクタータイルのバイナリデータ。</param>
    /// <returns>デコードされたベクタータイル。データが空または無効な場合は空のレイヤー一覧を持つタイル。</returns>
    public static VectorTile Decode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return new VectorTile();
        }

        byte[]? decompressedBuffer = null;
        ReadOnlySpan<byte> payload = data;

        // GZip 圧縮の検出と解凍
        if (data.Length >= 2 && data[0] == GzipMagic[0] && data[1] == GzipMagic[1])
        {
            decompressedBuffer = DecompressGzip(data);
            payload = decompressedBuffer;
        }

        var layers = new List<VectorTileLayer>();
        var reader = new ProtobufReader(payload);

        while (reader.TryReadTag(out int fieldNumber, out int wireType))
        {
            if (fieldNumber == 3 && wireType == 2) // Layer (tag 3, length-delimited)
            {
                if (reader.TryReadBytes(out ReadOnlySpan<byte> layerBytes))
                {
                    VectorTileLayer? layer = DecodeLayer(layerBytes);
                    if (layer is not null)
                    {
                        layers.Add(layer);
                    }
                }
            }
            else
            {
                reader.Skip(wireType);
            }
        }

        return new VectorTile { Layers = layers };
    }

    private static byte[] DecompressGzip(ReadOnlySpan<byte> compressed)
    {
        using var sourceStream = new MemoryStream(compressed.ToArray(), writable: false);
        using var gzipStream = new GZipStream(sourceStream, CompressionMode.Decompress);
        using var destStream = new MemoryStream();
        gzipStream.CopyTo(destStream);
        return destStream.ToArray();
    }

    private static VectorTileLayer? DecodeLayer(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtobufReader(bytes);

        string? name = null;
        uint extent = 4096;
        uint version = 2;
        var keys = new List<string>();
        var values = new List<object>();
        var featureBytesList = new List<byte[]>();

        while (reader.TryReadTag(out int fieldNumber, out int wireType))
        {
            switch (fieldNumber)
            {
                case 1 when wireType == 2: // name
                    if (reader.TryReadString(out string str))
                    {
                        name = str;
                    }
                    break;

                case 2 when wireType == 2: // features (repeated)
                    if (reader.TryReadBytes(out ReadOnlySpan<byte> fBytes))
                    {
                        featureBytesList.Add(fBytes.ToArray());
                    }
                    break;

                case 3 when wireType == 2: // keys (repeated string)
                    if (reader.TryReadString(out string key))
                    {
                        keys.Add(key);
                    }
                    break;

                case 4 when wireType == 2: // values (repeated Value)
                    if (reader.TryReadBytes(out ReadOnlySpan<byte> vBytes))
                    {
                        object? val = DecodeValue(vBytes);
                        values.Add(val ?? string.Empty);
                    }
                    break;

                case 5 when wireType == 0: // extent
                    if (reader.TryReadVarint(out ulong ext))
                    {
                        extent = (uint)ext;
                    }
                    break;

                case 15 when wireType == 0: // version
                    if (reader.TryReadVarint(out ulong ver))
                    {
                        version = (uint)ver;
                    }
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var features = new List<VectorTileFeature>(featureBytesList.Count);
        for (int i = 0; i < featureBytesList.Count; i++)
        {
            VectorTileFeature? feature = DecodeFeature(featureBytesList[i], keys, values);
            if (feature is not null)
            {
                features.Add(feature);
            }
        }

        return new VectorTileLayer
        {
            Name = name,
            Extent = extent,
            Version = version,
            Features = features,
        };
    }

    private static object? DecodeValue(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtobufReader(bytes);
        while (reader.TryReadTag(out int fieldNumber, out int wireType))
        {
            switch (fieldNumber)
            {
                case 1 when wireType == 2: // string_value
                    return reader.TryReadString(out string str) ? str : string.Empty;

                case 2 when wireType == 5: // float_value
                    return reader.TryReadFloat(out float flt) ? flt : 0f;

                case 3 when wireType == 1: // double_value
                    return reader.TryReadDouble(out double dbl) ? dbl : 0.0;

                case 4 when wireType == 0: // int_value
                    return reader.TryReadVarint(out ulong i64) ? (long)i64 : 0L;

                case 5 when wireType == 0: // uint_value
                    return reader.TryReadVarint(out ulong u64) ? u64 : 0UL;

                case 6 when wireType == 0: // sint_value (zigzag)
                    return reader.TryReadVarint(out ulong s64) ? DecodeZigzag64(s64) : 0L;

                case 7 when wireType == 0: // bool_value
                    return reader.TryReadVarint(out ulong b) && b != 0;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        return null;
    }

    private static VectorTileFeature? DecodeFeature(
        ReadOnlySpan<byte> bytes,
        List<string> keys,
        List<object> values)
    {
        var reader = new ProtobufReader(bytes);

        ulong id = 0;
        var geomType = VectorGeometryType.Unknown;
        var tagPairs = new List<uint>();
        var geometryCommands = new List<uint>();

        while (reader.TryReadTag(out int fieldNumber, out int wireType))
        {
            switch (fieldNumber)
            {
                case 1 when wireType == 0: // id
                    if (reader.TryReadVarint(out ulong fid))
                    {
                        id = fid;
                    }
                    break;

                case 2 when wireType == 2: // tags (packed repeated uint32)
                    if (reader.TryReadBytes(out ReadOnlySpan<byte> tagBytes))
                    {
                        var tagReader = new ProtobufReader(tagBytes);
                        while (tagReader.TryReadVarint(out ulong t))
                        {
                            tagPairs.Add((uint)t);
                        }
                    }
                    break;

                case 2 when wireType == 0: // tags (unpacked fallback)
                    if (reader.TryReadVarint(out ulong singleTag))
                    {
                        tagPairs.Add((uint)singleTag);
                    }
                    break;

                case 3 when wireType == 0: // type (GeomType)
                    if (reader.TryReadVarint(out ulong gt))
                    {
                        geomType = (VectorGeometryType)gt;
                    }
                    break;

                case 4 when wireType == 2: // geometry (packed repeated uint32)
                    if (reader.TryReadBytes(out ReadOnlySpan<byte> geomBytes))
                    {
                        var geomReader = new ProtobufReader(geomBytes);
                        while (geomReader.TryReadVarint(out ulong cmd))
                        {
                            geometryCommands.Add((uint)cmd);
                        }
                    }
                    break;

                case 4 when wireType == 0: // geometry (unpacked fallback)
                    if (reader.TryReadVarint(out ulong singleCmd))
                    {
                        geometryCommands.Add((uint)singleCmd);
                    }
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        // プロパティ辞書の構築
        var properties = new Dictionary<string, object>(tagPairs.Count / 2, StringComparer.Ordinal);
        for (int i = 0; i + 1 < tagPairs.Count; i += 2)
        {
            int keyIndex = (int)tagPairs[i];
            int valIndex = (int)tagPairs[i + 1];

            if (keyIndex >= 0 && keyIndex < keys.Count && valIndex >= 0 && valIndex < values.Count)
            {
                properties[keys[keyIndex]] = values[valIndex];
            }
        }

        // ジオメトリのデコード
        IReadOnlyList<IReadOnlyList<VectorPoint>> geometryParts = DecodeGeometry(geometryCommands, geomType);

        return new VectorTileFeature
        {
            Id = id,
            GeometryType = geomType,
            Properties = properties,
            GeometryParts = geometryParts,
        };
    }

    private static List<IReadOnlyList<VectorPoint>> DecodeGeometry(
        List<uint> commands,
        VectorGeometryType geomType)
    {
        var parts = new List<IReadOnlyList<VectorPoint>>();
        var currentPart = new List<VectorPoint>();

        int cursorX = 0;
        int cursorY = 0;
        int index = 0;

        while (index < commands.Count)
        {
            uint commandInteger = commands[index++];
            uint cmd = commandInteger & 0x7;
            uint count = commandInteger >> 3;

            switch (cmd)
            {
                case 1: // MoveTo
                    for (int i = 0; i < count && index + 1 < commands.Count; i++)
                    {
                        int dx = DecodeZigzag32(commands[index++]);
                        int dy = DecodeZigzag32(commands[index++]);
                        cursorX += dx;
                        cursorY += dy;

                        if (geomType == VectorGeometryType.Point)
                        {
                            // Point / MultiPoint の場合、各 MoveTo 点が独立した点
                            parts.Add([new VectorPoint(cursorX, cursorY)]);
                        }
                        else
                        {
                            if (currentPart.Count > 0)
                            {
                                parts.Add(currentPart);
                                currentPart = new List<VectorPoint>();
                            }
                            currentPart.Add(new VectorPoint(cursorX, cursorY));
                        }
                    }
                    break;

                case 2: // LineTo
                    for (int i = 0; i < count && index + 1 < commands.Count; i++)
                    {
                        int dx = DecodeZigzag32(commands[index++]);
                        int dy = DecodeZigzag32(commands[index++]);
                        cursorX += dx;
                        cursorY += dy;
                        currentPart.Add(new VectorPoint(cursorX, cursorY));
                    }
                    break;

                case 7: // ClosePath
                    if (currentPart.Count > 0)
                    {
                        // ポリゴンの閉合点（始点と同一座標）を追加
                        if (currentPart[0] != currentPart[^1])
                        {
                            currentPart.Add(currentPart[0]);
                        }

                        parts.Add(currentPart);
                        currentPart = new List<VectorPoint>();
                    }
                    break;

                default:
                    // 不明なコマンドは安全に終了
                    index = commands.Count;
                    break;
            }
        }

        if (currentPart.Count > 0)
        {
            parts.Add(currentPart);
        }

        return parts;
    }

    private static int DecodeZigzag32(uint value)
    {
        return (int)((value >> 1) ^ (-(int)(value & 1)));
    }

    private static long DecodeZigzag64(ulong value)
    {
        long v = (long)value;
        return (v >> 1) ^ -(v & 1);
    }

    private ref struct ProtobufReader(ReadOnlySpan<byte> buffer)
    {
        private readonly ReadOnlySpan<byte> span = buffer;
        private int position = 0;

        public bool HasMore => this.position < this.span.Length;

        public bool TryReadTag(out int fieldNumber, out int wireType)
        {
            if (!this.TryReadVarint(out ulong tag))
            {
                fieldNumber = 0;
                wireType = 0;
                return false;
            }

            fieldNumber = (int)(tag >> 3);
            wireType = (int)(tag & 0x7);
            return fieldNumber > 0;
        }

        public bool TryReadVarint(out ulong value)
        {
            value = 0;
            int shift = 0;

            while (this.position < this.span.Length)
            {
                byte b = this.span[this.position++];
                value |= (ulong)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                {
                    return true;
                }

                shift += 7;
                if (shift >= 64)
                {
                    return false;
                }
            }

            return false;
        }

        public bool TryReadBytes(out ReadOnlySpan<byte> result)
        {
            if (!this.TryReadVarint(out ulong length))
            {
                result = default;
                return false;
            }

            int len = (int)length;
            if (len < 0 || this.position + len > this.span.Length)
            {
                result = default;
                return false;
            }

            result = this.span.Slice(this.position, len);
            this.position += len;
            return true;
        }

        public bool TryReadString(out string result)
        {
            if (!this.TryReadBytes(out ReadOnlySpan<byte> bytes))
            {
                result = string.Empty;
                return false;
            }

            result = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public bool TryReadFloat(out float result)
        {
            if (this.position + 4 > this.span.Length)
            {
                result = 0f;
                return false;
            }

            result = BinaryPrimitives.ReadSingleLittleEndian(this.span.Slice(this.position, 4));
            this.position += 4;
            return true;
        }

        public bool TryReadDouble(out double result)
        {
            if (this.position + 8 > this.span.Length)
            {
                result = 0.0;
                return false;
            }

            result = BinaryPrimitives.ReadDoubleLittleEndian(this.span.Slice(this.position, 8));
            this.position += 8;
            return true;
        }

        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0: // Varint
                    this.TryReadVarint(out _);
                    break;
                case 1: // Fixed64
                    this.position = Math.Min(this.position + 8, this.span.Length);
                    break;
                case 2: // Length-delimited
                    if (this.TryReadVarint(out ulong len))
                    {
                        this.position = Math.Min(this.position + (int)len, this.span.Length);
                    }
                    break;
                case 5: // Fixed32
                    this.position = Math.Min(this.position + 4, this.span.Length);
                    break;
                default:
                    // 予期しないワイヤータイプの場合は安全のため末尾へスキップ
                    this.position = this.span.Length;
                    break;
            }
        }
    }
}
