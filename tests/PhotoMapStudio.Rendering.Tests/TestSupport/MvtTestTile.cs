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

    /// <summary>複数のタイルのレイヤーを 1 つのタイルにまとめる（MVT のタイルは、レイヤーの繰り返しなので連結でよい）。</summary>
    public static byte[] Combine(params byte[][] tiles) => [.. tiles.SelectMany(static t => t)];

    /// <summary>
    /// <c>transportation</c> レイヤーに、<c>class</c> 属性つきの折れ線を持つタイルを作る（extent 4096 の座標）。
    /// 1 つの地物は 1 つの折れ線（1 パート）。
    /// </summary>
    public static byte[] CreateTransportation(params (string RoadClass, (int X, int Y)[] Points)[] lines)
        => CreateRoadLayer("transportation", lines);

    /// <summary><see cref="CreateTransportation"/> と同じ内容を、指定した名前のレイヤー（例: <c>transportation_name</c>）に持つタイルを作る。</summary>
    public static byte[] CreateRoadLayer(string layerName, params (string RoadClass, (int X, int Y)[] Points)[] lines)
    {
        using var layerStream = new MemoryStream();
        WriteTag(layerStream, 1, 2);
        WriteString(layerStream, layerName);

        for (int f = 0; f < lines.Length; f++)
        {
            (_, (int X, int Y)[] points) = lines[f];

            using var geometry = new MemoryStream();
            WriteVarint(geometry, (1 << 3) | 1); // MoveTo 1
            WriteVarint(geometry, EncodeZigzag32(points[0].X));
            WriteVarint(geometry, EncodeZigzag32(points[0].Y));
            WriteVarint(geometry, (uint)(((points.Length - 1) << 3) | 2)); // LineTo n-1
            for (int i = 1; i < points.Length; i++)
            {
                WriteVarint(geometry, EncodeZigzag32(points[i].X - points[i - 1].X));
                WriteVarint(geometry, EncodeZigzag32(points[i].Y - points[i - 1].Y));
            }

            // tags = [key 0 (class), value f]
            using var tags = new MemoryStream();
            WriteVarint(tags, 0);
            WriteVarint(tags, (ulong)f);

            using var feature = new MemoryStream();
            WriteTag(feature, 2, 2);
            WriteBytes(feature, tags.ToArray());
            WriteTag(feature, 3, 0);
            WriteVarint(feature, 2); // LineString
            WriteTag(feature, 4, 2);
            WriteBytes(feature, geometry.ToArray());

            WriteTag(layerStream, 2, 2);
            WriteBytes(layerStream, feature.ToArray());
        }

        // keys = ["class"]
        WriteTag(layerStream, 3, 2);
        WriteString(layerStream, "class");

        // values = 各地物の class（Value.string_value = field 1）
        foreach ((string roadClass, _) in lines)
        {
            using var value = new MemoryStream();
            WriteTag(value, 1, 2);
            WriteString(value, roadClass);
            WriteTag(layerStream, 4, 2);
            WriteBytes(layerStream, value.ToArray());
        }

        // extent = 4096
        WriteTag(layerStream, 5, 0);
        WriteVarint(layerStream, 4096);

        using var stream = new MemoryStream();
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
