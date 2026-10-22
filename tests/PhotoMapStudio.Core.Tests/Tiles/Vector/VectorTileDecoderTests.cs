using System.IO.Compression;

using PhotoMapStudio.Core.Tiles.Vector;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class VectorTileDecoderTests
{
    [Fact]
    public void 空のバイト列を渡した場合は空のタイルを返す()
    {
        VectorTile tile = VectorTileDecoder.Decode([]);
        Assert.Empty(tile.Layers);
    }

    [Fact]
    public void MVTポリゴン地物とプロパティを正しくデコードできる()
    {
        byte[] pbf = CreateSampleWaterPolygonTile();

        VectorTile tile = VectorTileDecoder.Decode(pbf);

        Assert.Single(tile.Layers);
        VectorTileLayer layer = tile.Layers[0];
        Assert.Equal("water", layer.Name);
        Assert.Equal(4096u, layer.Extent);
        Assert.Equal(2u, layer.Version);

        Assert.Single(layer.Features);
        VectorTileFeature feature = layer.Features[0];
        Assert.Equal(123ul, feature.Id);
        Assert.Equal(VectorGeometryType.Polygon, feature.GeometryType);
        Assert.Equal("ocean", feature.GetString("class"));

        // ジオメトリ: 三角形外輪 (10, 10) -> (30, 10) -> (30, 30) -> (10, 10)
        Assert.Single(feature.GeometryParts);
        IReadOnlyList<VectorPoint> ring = feature.GeometryParts[0];
        Assert.Equal(4, ring.Count);
        Assert.Equal(new VectorPoint(10, 10), ring[0]);
        Assert.Equal(new VectorPoint(30, 10), ring[1]);
        Assert.Equal(new VectorPoint(30, 30), ring[2]);
        Assert.Equal(new VectorPoint(10, 10), ring[3]); // 閉合点
    }

    [Fact]
    public void GZip圧縮されたMVTデータも透過的に解凍してデコードできる()
    {
        byte[] pbf = CreateSampleWaterPolygonTile();
        byte[] gzipped = CompressGzip(pbf);

        VectorTile tile = VectorTileDecoder.Decode(gzipped);

        Assert.Single(tile.Layers);
        Assert.Equal("water", tile.Layers[0].Name);
        Assert.Equal("ocean", tile.Layers[0].Features[0].GetString("class"));
    }

    [Fact]
    public void Point地物と複数プロパティ型を正しくデコードできる()
    {
        byte[] pbf = CreateSamplePoiPointTile();

        VectorTile tile = VectorTileDecoder.Decode(pbf);

        VectorTileLayer? layer = tile.GetLayer("poi");
        Assert.NotNull(layer);
        Assert.Single(layer.Features);
        VectorTileFeature feature = layer.Features[0];
        Assert.Equal(VectorGeometryType.Point, feature.GeometryType);
        Assert.Equal("東京駅", feature.GetString("name:ja"));
        Assert.Equal("station", feature.GetString("class"));

        Assert.Single(feature.GeometryParts);
        Assert.Equal(new VectorPoint(500, 600), feature.GeometryParts[0][0]);
    }

    private static byte[] CreateSampleWaterPolygonTile()
    {
        using var stream = new MemoryStream();
        // Layer メッセージの構築
        using var layerStream = new MemoryStream();

        // tag 1 (name = "water")
        WriteTag(layerStream, 1, 2);
        WriteString(layerStream, "water");

        // tag 3 (keys = ["class"])
        WriteTag(layerStream, 3, 2);
        WriteString(layerStream, "class");

        // tag 4 (values = [Value(string_value="ocean")])
        using var valStream = new MemoryStream();
        WriteTag(valStream, 1, 2);
        WriteString(valStream, "ocean");
        WriteTag(layerStream, 4, 2);
        WriteBytes(layerStream, valStream.ToArray());

        // tag 5 (extent = 4096)
        WriteTag(layerStream, 5, 0);
        WriteVarint(layerStream, 4096);

        // tag 15 (version = 2)
        WriteTag(layerStream, 15, 0);
        WriteVarint(layerStream, 2);

        // tag 2 (feature)
        using var featStream = new MemoryStream();
        // id = 123
        WriteTag(featStream, 1, 0);
        WriteVarint(featStream, 123);

        // tags = [0, 0] (keyIndex 0 = "class", valIndex 0 = "ocean")
        using var tagStream = new MemoryStream();
        WriteVarint(tagStream, 0);
        WriteVarint(tagStream, 0);
        WriteTag(featStream, 2, 2);
        WriteBytes(featStream, tagStream.ToArray());

        // type = 3 (Polygon)
        WriteTag(featStream, 3, 0);
        WriteVarint(featStream, 3);

        // geometry = MoveTo(1, 10, 10), LineTo(2, 20, 0, 0, 20), ClosePath(7, 1)
        using var geomStream = new MemoryStream();
        // MoveTo (cmd 1, count 1) => (1 << 3) | 1 = 9
        WriteVarint(geomStream, (1 << 3) | 1);
        WriteVarint(geomStream, EncodeZigzag32(10));
        WriteVarint(geomStream, EncodeZigzag32(10));
        // LineTo (cmd 2, count 2) => (2 << 3) | 2 = 18
        WriteVarint(geomStream, (2 << 3) | 2);
        WriteVarint(geomStream, EncodeZigzag32(20)); // x += 20 -> 30
        WriteVarint(geomStream, EncodeZigzag32(0));  // y += 0  -> 10
        WriteVarint(geomStream, EncodeZigzag32(0));  // x += 0  -> 30
        WriteVarint(geomStream, EncodeZigzag32(20)); // y += 20 -> 30
        // ClosePath (cmd 7, count 1) => (1 << 3) | 7 = 15
        WriteVarint(geomStream, (1 << 3) | 7);

        WriteTag(featStream, 4, 2);
        WriteBytes(featStream, geomStream.ToArray());

        WriteTag(layerStream, 2, 2);
        WriteBytes(layerStream, featStream.ToArray());

        // Tile メッセージ: tag 3 (Layer)
        WriteTag(stream, 3, 2);
        WriteBytes(stream, layerStream.ToArray());

        return stream.ToArray();
    }

    private static byte[] CreateSamplePoiPointTile()
    {
        using var stream = new MemoryStream();
        using var layerStream = new MemoryStream();

        WriteTag(layerStream, 1, 2);
        WriteString(layerStream, "poi");

        // keys = ["name:ja", "class"]
        WriteTag(layerStream, 3, 2);
        WriteString(layerStream, "name:ja");
        WriteTag(layerStream, 3, 2);
        WriteString(layerStream, "class");

        // values = ["東京駅", "station"]
        using (var v1 = new MemoryStream())
        {
            WriteTag(v1, 1, 2);
            WriteString(v1, "東京駅");
            WriteTag(layerStream, 4, 2);
            WriteBytes(layerStream, v1.ToArray());
        }

        using (var v2 = new MemoryStream())
        {
            WriteTag(v2, 1, 2);
            WriteString(v2, "station");
            WriteTag(layerStream, 4, 2);
            WriteBytes(layerStream, v2.ToArray());
        }

        // Feature
        using var featStream = new MemoryStream();
        WriteTag(featStream, 3, 0);
        WriteVarint(featStream, 1); // Point

        // tags = [0, 0, 1, 1]
        using var tagStream = new MemoryStream();
        WriteVarint(tagStream, 0); // "name:ja"
        WriteVarint(tagStream, 0); // "東京駅"
        WriteVarint(tagStream, 1); // "class"
        WriteVarint(tagStream, 1); // "station"
        WriteTag(featStream, 2, 2);
        WriteBytes(featStream, tagStream.ToArray());

        // geometry = MoveTo(1, 500, 600) => (1 << 3) | 1 = 9
        using var geomStream = new MemoryStream();
        WriteVarint(geomStream, (1 << 3) | 1);
        WriteVarint(geomStream, EncodeZigzag32(500));
        WriteVarint(geomStream, EncodeZigzag32(600));
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
        uint tag = (uint)((fieldNumber << 3) | wireType);
        WriteVarint(stream, tag);
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

    private static byte[] CompressGzip(byte[] data)
    {
        using var dest = new MemoryStream();
        using (var gzip = new GZipStream(dest, CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }

        return dest.ToArray();
    }
}
