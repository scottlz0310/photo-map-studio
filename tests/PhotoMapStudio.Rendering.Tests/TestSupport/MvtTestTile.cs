namespace PhotoMapStudio.Rendering.Tests.TestSupport;

/// <summary>テスト用の MVT（Mapbox Vector Tile）を組み立てる。</summary>
internal static class MvtTestTile
{
    /// <summary>
    /// <c>water</c> レイヤーに、タイルの左端から <paramref name="rightEdge"/>（extent 4096 の座標）までを覆う長方形 1 つを持つタイルを作る。
    /// 既定は全域。
    /// </summary>
    public static byte[] CreateWater(int rightEdge = 4096)
    {
        using var stream = new MemoryStream();
        using var layerStream = new MemoryStream();

        // name = "water"
        WriteTag(layerStream, 1, 2);
        WriteString(layerStream, "water");

        // extent = 4096
        WriteTag(layerStream, 5, 0);
        WriteVarint(layerStream, 4096);

        // Feature (全域を覆うポリゴン)
        using var featStream = new MemoryStream();
        WriteTag(featStream, 3, 0);
        WriteVarint(featStream, 3); // Polygon

        using var geomStream = new MemoryStream();
        WriteVarint(geomStream, (1 << 3) | 1); // MoveTo 1
        WriteVarint(geomStream, EncodeZigzag32(0));
        WriteVarint(geomStream, EncodeZigzag32(0));

        WriteVarint(geomStream, (3 << 3) | 2); // LineTo 3
        WriteVarint(geomStream, EncodeZigzag32(rightEdge)); // -> (rightEdge, 0)
        WriteVarint(geomStream, EncodeZigzag32(0));
        WriteVarint(geomStream, EncodeZigzag32(0));    // -> (4096, 4096)
        WriteVarint(geomStream, EncodeZigzag32(4096));
        WriteVarint(geomStream, EncodeZigzag32(-rightEdge)); // -> (0, 4096)
        WriteVarint(geomStream, EncodeZigzag32(0));

        WriteVarint(geomStream, (1 << 3) | 7); // ClosePath 1

        WriteTag(featStream, 4, 2);
        WriteBytes(featStream, geomStream.ToArray());

        WriteTag(layerStream, 2, 2);
        WriteBytes(layerStream, featStream.ToArray());

        WriteTag(stream, 3, 2);
        WriteBytes(stream, layerStream.ToArray());

        return stream.ToArray();
    }

    private static void WriteTag(Stream stream, int fieldNumber, int wireType)
    {
        WriteVarint(stream, (uint)((fieldNumber << 3) | wireType));
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static void WriteBytes(Stream stream, byte[] bytes)
    {
        WriteVarint(stream, (ulong)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteString(Stream stream, string str)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(str);
        WriteBytes(stream, bytes);
    }

    private static uint EncodeZigzag32(int n)
    {
        return (uint)((n << 1) ^ (n >> 31));
    }
}
