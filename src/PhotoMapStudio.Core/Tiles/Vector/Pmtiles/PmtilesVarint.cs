namespace PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

/// <summary>
/// PMTiles 仕様で使用される可変長整数 (LEB128 Varint) の読み書きユーティリティを提供します。
/// </summary>
public static class PmtilesVarint
{
    /// <summary>
    /// バッファから 64-bit 符号なし可変長整数を読み取ります。
    /// </summary>
    public static ulong ReadVarint(ReadOnlySpan<byte> buffer, ref int offset)
    {
        ulong result = 0;
        int shift = 0;

        while (offset < buffer.Length)
        {
            byte b = buffer[offset++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift > 63)
            {
                throw new FormatException("Varint が 64 ビットの表現範囲を超えました。");
            }
        }

        throw new FormatException("バッファの終端に達しましたが Varint が終了しませんでした。");
    }

    /// <summary>
    /// 64-bit 符号なし可変長整数をバッファに書き込みます。
    /// </summary>
    public static void WriteVarint(ulong value, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        while (value >= 0x80)
        {
            destination.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        destination.WriteByte((byte)(value & 0x7F));
    }
}
