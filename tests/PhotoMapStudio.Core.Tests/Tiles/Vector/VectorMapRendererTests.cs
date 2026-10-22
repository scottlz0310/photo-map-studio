using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Tiles.Vector;

public class VectorMapRendererTests
{
    [Fact]
    public async Task 有効な座標とサイズでビットマップ画像を生成できる()
    {
        // 皇居付近の座標 (35.685, 139.752)
        var center = new GeoCoordinate(35.685, 139.752);
        int requestedZoom = 14;
        int width = 256;
        int height = 256;

        var requestedTiles = new List<(int z, int x, int y)>();
        byte[] sampleWaterTile = CreateWaterTile();

        var mockSource = new MockVectorTileSource(
            maxZoom: 14,
            fetchHandler: (z, x, y) =>
            {
                requestedTiles.Add((z, x, y));
                return sampleWaterTile;
            });

        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(mockSource, style);

        using SKBitmap bitmap = await renderer.RenderMapAsync(center, requestedZoom, width, height, CancellationToken.None);

        Assert.NotNull(bitmap);
        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
        Assert.NotEmpty(requestedTiles);

        // すべての要求タイルが z=14 であること
        Assert.All(requestedTiles, t => Assert.Equal(14, t.z));

        // 水域（#A5D6E7）が描画されていること
        SKColor samplePixel = bitmap.GetPixel(width / 2, height / 2);
        Assert.Equal(new SKColor(0xA5, 0xD6, 0xE7), samplePixel);
    }

    [Fact]
    public async Task sourceのMaxZoomを超えるズームレベルでは過拡大としてMaxZoomのタイルを要求する()
    {
        var center = new GeoCoordinate(35.685, 139.752);
        int requestedZoom = 16; // 要求ズーム 16
        int sourceMaxZoom = 14; // 提供 maxzoom 14

        var requestedTiles = new List<(int z, int x, int y)>();
        var mockSource = new MockVectorTileSource(
            maxZoom: sourceMaxZoom,
            fetchHandler: (z, x, y) =>
            {
                requestedTiles.Add((z, x, y));
                return CreateWaterTile();
            });

        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(mockSource, style);

        using SKBitmap bitmap = await renderer.RenderMapAsync(center, requestedZoom, 128, 128, CancellationToken.None);

        Assert.Equal(128, bitmap.Width);
        Assert.Equal(128, bitmap.Height);

        // 要求ズーム 16 に対して、ソースへの要求タイルは z=14 に制限されていること
        Assert.NotEmpty(requestedTiles);
        Assert.All(requestedTiles, t => Assert.Equal(sourceMaxZoom, t.z));
    }

    [Theory]
    [InlineData(0, 100, 14)]
    [InlineData(100, 0, 14)]
    [InlineData(-10, 100, 14)]
    [InlineData(100, -10, 14)]
    [InlineData(100, 100, -1)]
    public async Task 不正な引数を渡すとArgumentOutOfRangeExceptionをスローする(int width, int height, int zoom)
    {
        var mockSource = new MockVectorTileSource(14, (_, _, _) => null);
        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(mockSource, style);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => renderer.RenderMapAsync(new GeoCoordinate(35.0, 135.0), zoom, width, height, CancellationToken.None));
    }

    private static byte[] CreateWaterTile()
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
        WriteVarint(geomStream, EncodeZigzag32(4096)); // -> (4096, 0)
        WriteVarint(geomStream, EncodeZigzag32(0));
        WriteVarint(geomStream, EncodeZigzag32(0));    // -> (4096, 4096)
        WriteVarint(geomStream, EncodeZigzag32(4096));
        WriteVarint(geomStream, EncodeZigzag32(-4096)); // -> (0, 4096)
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

    private sealed class MockVectorTileSource(int maxZoom, Func<int, int, int, byte[]?> fetchHandler) : IVectorTileSource
    {
        public string SourceId => "mock-source";
        public int MaxZoom => maxZoom;

        public Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(fetchHandler(z, x, y));
        }
    }
}
