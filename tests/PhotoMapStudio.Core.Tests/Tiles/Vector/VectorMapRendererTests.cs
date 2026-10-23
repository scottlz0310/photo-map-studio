using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Tiles;
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
    public async Task TileSourceの指定に応じて適切なソースとスタイルに切り替えて描画する()
    {
        var center = new GeoCoordinate(35.685, 139.752);
        byte[] ofmTile = CreateWaterTile(); // 水色 #A5D6E7
        byte[] gsiTile = CreateWaterTile(); // テスト用ダミー

        var ofmSource = new MockVectorTileSource(14, (z, x, y) => ofmTile);
        var gsiSource = new MockVectorTileSource(16, (z, x, y) => gsiTile);
        using var ofmStyle = new OpenMapTilesStyle();
        using var gsiStyle = new GsiVectorStyle();

        var renderer = new VectorMapRenderer(
            defaultTileSource: ofmSource,
            defaultStyle: ofmStyle,
            resolver: source => source == TileSources.GsiVector ? (gsiSource, gsiStyle) : (ofmSource, ofmStyle));

        // 1. GsiVector 指定時
        using var gsiBitmap = await renderer.RenderMapAsync(
            center, 14, 256, 256, session: null, tileSource: TileSources.GsiVector, CancellationToken.None);
        Assert.NotNull(gsiBitmap);
        // GSI 背景色は #F8F7F2
        Assert.Equal(new SKColor(0xF8, 0xF7, 0xF2), gsiStyle.BackgroundColor);

        // 2. OpenFreeMap 指定時
        using var ofmBitmap = await renderer.RenderMapAsync(
            center, 14, 256, 256, session: null, tileSource: TileSources.OpenFreeMap, CancellationToken.None);
        Assert.NotNull(ofmBitmap);
        Assert.Equal(new SKColor(0xF2, 0xEF, 0xE9), ofmStyle.BackgroundColor);
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

    [Fact]
    public async Task 日付変更線付近および低ズームで範囲外のXを要求せず世界幅でwrapする()
    {
        // 中心: 緯度 0, 経度 179.99 度、zoom = 2
        var center = new GeoCoordinate(0.0, 179.99);
        int zoom = 2;
        int width = 256;
        int height = 256;

        var requestedTiles = new List<(int z, int x, int y)>();
        var mockSource = new MockVectorTileSource(
            maxZoom: 14,
            fetchHandler: (z, x, y) =>
            {
                requestedTiles.Add((z, x, y));
                return CreateWaterTile();
            });

        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(mockSource, style);

        using SKBitmap bitmap = await renderer.RenderMapAsync(center, zoom, width, height, CancellationToken.None);

        Assert.NotNull(bitmap);
        Assert.NotEmpty(requestedTiles);

        int maxTiles = 1 << zoom; // 2^2 = 4
        foreach (var (z, x, y) in requestedTiles)
        {
            Assert.Equal(zoom, z);
            // X は 0〜3 の範囲内に正規化（wrap）されていること (x=4 などにならない)
            Assert.InRange(x, 0, maxTiles - 1);
            // Y は 0〜3 の範囲内であること
            Assert.InRange(y, 0, maxTiles - 1);
        }

        // 東側にまたいだ x = 0 のタイルが要求されていること
        Assert.Contains(requestedTiles, t => t.x == 0);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 描画できるタイルが1枚もない場合は配信範囲外として404のTileFetchExceptionをスローする(bool useEmptyBytes)
    {
        // パリ付近。地理院ベクトルは国外のタイルを配信しない（PMTiles に該当エントリがなく null）
        var center = new GeoCoordinate(48.8584, 2.2945);
        var mockSource = new MockVectorTileSource(14, (_, _, _) => useEmptyBytes ? [] : null);
        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(mockSource, style);

        TileFetchException exception = await Assert.ThrowsAsync<TileFetchException>(
            () => renderer.RenderMapAsync(center, 14, 256, 256, session: null, tileSource: TileSources.GsiVector, CancellationToken.None));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
        Assert.NotNull(exception.RequestUri);
    }

    [Fact]
    public async Task 一部のタイルだけ存在する場合は存在するタイルで描画し例外にしない()
    {
        // 配信範囲の端で一部のタイルだけ欠ける場合は、描画できる範囲を描く
        var center = new GeoCoordinate(35.685, 139.752);
        byte[] waterTile = CreateWaterTile();
        int calls = 0;
        var mockSource = new MockVectorTileSource(14, (_, _, _) => calls++ == 0 ? waterTile : null);
        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(mockSource, style);

        using SKBitmap bitmap = await renderer.RenderMapAsync(center, 14, 512, 512, CancellationToken.None);

        Assert.True(calls > 1);
        Assert.Equal(512, bitmap.Width);
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

        public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(fetchHandler(z, x, y));
        }
    }
}
