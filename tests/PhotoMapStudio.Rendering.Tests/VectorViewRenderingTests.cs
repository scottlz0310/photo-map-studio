using System.Net;

using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

public class VectorViewRenderingTests
{
    private const int BaseZoom = 14;
    private const int TileX = 14370;
    private const int TileY = 6488;
    private static readonly SKColor WaterColor = new(0xA0, 0xC8, 0xF0);

    [Theory]
    [InlineData(14.0)]
    [InlineData(14.25)]
    [InlineData(14.5)]
    [InlineData(14.75)]
    public async Task 連続ズームで境界の位置が拡大率どおりになる(double zoom)
    {
        // タイルの中心を画像の中心に置く。水域は左端から 3/4 まで（中心から 1/4 タイル幅の位置が境界）
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);
        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater(rightEdge: 3072)), style);

        using VectorViewResult result = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, zoom, 256, 256), CancellationToken.None);

        double boundaryOffset = 64 * Math.Pow(2, zoom - BaseZoom);
        using SKBitmap bitmap = SKBitmap.FromImage(result.Image);
        int boundary = 128 + (int)Math.Round(boundaryOffset);
        Assert.Equal(WaterColor, bitmap.GetPixel(boundary - 3, 128));
        Assert.Equal(style.BackgroundColor, bitmap.GetPixel(boundary + 3, 128));
        Assert.Equal(WaterColor, bitmap.GetPixel(10, 128));
    }

    [Theory]
    [InlineData(14.0, 14)]
    [InlineData(14.99, 14)]
    [InlineData(15.0, 15)]
    [InlineData(16.5, 16)]
    public async Task スタイルには切り捨てた整数のズームを渡す(double zoom, int expectedStyleZoom)
    {
        var style = new RecordingStyle();
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), style);
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);

        using VectorViewResult result = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, zoom, 64, 64), CancellationToken.None);

        Assert.NotEmpty(style.Zooms);
        Assert.All(style.Zooms, z => Assert.Equal(expectedStyleZoom, z));
    }

    [Fact]
    public async Task 整数ズームは従来のAPIと同じ画素になる()
    {
        (double latitude, double longitude) = TileToLatLon(TileX + 0.3, TileY + 0.7, BaseZoom);
        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater(rightEdge: 3072)), style);

        using SKBitmap legacy = await renderer.RenderMapAsync(latitude, longitude, BaseZoom, 200, 150, CancellationToken.None);
        using VectorViewResult view = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, BaseZoom, 200, 150), CancellationToken.None);

        // 既存の API は RGBA、新しい API は実行環境の色形式（Windows は BGRA）で描く。
        // 色形式が違うと、境界の半透明の画素の丸めが 1 だけずれ得るため、各チャンネルの差 1 までを許す
        using SKBitmap actual = SKBitmap.FromImage(view.Image);
        for (int y = 0; y < 150; y++)
        {
            for (int x = 0; x < 200; x++)
            {
                SKColor expected = legacy.GetPixel(x, y);
                SKColor pixel = actual.GetPixel(x, y);
                Assert.InRange(Math.Abs(expected.Red - pixel.Red), 0, 1);
                Assert.InRange(Math.Abs(expected.Green - pixel.Green), 0, 1);
                Assert.InRange(Math.Abs(expected.Blue - pixel.Blue), 0, 1);
                Assert.Equal(expected.Alpha, pixel.Alpha);
            }
        }
    }

    [Theory]
    [InlineData(301, 177)]
    [InlineData(640, 480)]
    [InlineData(1, 1)]
    public async Task 画像の寸法は指定した物理ピクセルどおりになる(int width, int height)
    {
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);
        using var style = new OpenMapTilesStyle();
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), style);

        using VectorViewResult result = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, 14.4, width, height), CancellationToken.None);

        Assert.Equal(width, result.Image.Width);
        Assert.Equal(height, result.Image.Height);
    }

    [Fact]
    public async Task 開始前にキャンセルされていれば画像を返さない()
    {
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), new RecordingStyle());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => renderer.RenderViewAsync(new VectorViewRequest(35, 139, 14, 64, 64), cts.Token));
    }

    [Fact]
    public async Task 描画の途中でキャンセルされれば以降のパスを描かず画像を返さない()
    {
        using var cts = new CancellationTokenSource();
        var style = new RecordingStyle { OnCall = pass => { if (pass == "polygons") { cts.Cancel(); } } };
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), style);
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => renderer.RenderViewAsync(new VectorViewRequest(latitude, longitude, 14, 64, 64), cts.Token));

        Assert.Equal(["polygons"], style.Calls);
    }

    [Fact]
    public async Task 最後のパスの描画中にキャンセルされても画像を返さない()
    {
        using var cts = new CancellationTokenSource();
        var style = new RecordingStyle { OnCall = pass => { if (pass == "labels") { cts.Cancel(); } } };
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), style);
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => renderer.RenderViewAsync(new VectorViewRequest(latitude, longitude, 14, 64, 64), cts.Token));

        Assert.Equal("labels", style.Calls[^1]);
    }

    [Fact]
    public async Task 全てのタイルを取得できれば欠落はない()
    {
        (double latitude, double longitude) = TileToLatLon(TileX, TileY, BaseZoom);
        var renderer = new VectorMapRenderer(
            new MockVectorTileSource(BaseZoom, (_, _, _) => MvtTestTile.CreateWater()), new RecordingStyle());

        using VectorViewResult result = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, 14, 128, 128), CancellationToken.None);

        Assert.Equal(4, result.RequestedTileCount);
        Assert.Equal(0, result.MissingTileCount);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task 配信範囲外のタイルは欠落として判別できる()
    {
        // タイルの角が画像の中心で、4 枚を要求する。描けるのは 1 枚だけ
        (double latitude, double longitude) = TileToLatLon(TileX, TileY, BaseZoom);
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), new RecordingStyle());

        using VectorViewResult result = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, 14, 128, 128), CancellationToken.None);

        Assert.Equal(4, result.RequestedTileCount);
        Assert.Equal(3, result.AbsentTileCount);
        Assert.Equal(0, result.UnavailableTileCount);
        Assert.Equal(3, result.MissingTileCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task 一時的な通信失敗は許可したときだけ欠落として判別できる()
    {
        (double latitude, double longitude) = TileToLatLon(TileX, TileY, BaseZoom);
        var source = new MockVectorTileSource(BaseZoom, (_, x, y) => x == TileX && y == TileY
            ? MvtTestTile.CreateWater()
            : throw new TileFetchException("通信失敗", new Uri("https://example.test/tile"), HttpStatusCode.ServiceUnavailable, null));
        var renderer = new VectorMapRenderer(source, new RecordingStyle());
        var request = new VectorViewRequest(latitude, longitude, 14, 128, 128);

        using VectorViewResult result = await renderer.RenderViewAsync(request with { AllowIncompleteMaps = true }, CancellationToken.None);
        Assert.Equal(3, result.UnavailableTileCount);
        Assert.False(result.IsComplete);

        // 許可しなければ、失敗を隠さず例外にする
        await Assert.ThrowsAsync<TileFetchException>(() => renderer.RenderViewAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task 結果を破棄すると画像も破棄される()
    {
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), new RecordingStyle());
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);
        VectorViewResult result = await renderer.RenderViewAsync(new VectorViewRequest(latitude, longitude, 14, 64, 64));
        SKImage image = result.Image;

        result.Dispose();

        Assert.Equal(IntPtr.Zero, image.Handle);
        Assert.Throws<ObjectDisposedException>(() => result.Image);
    }

    [Fact]
    public async Task 画像を取り出すと所有権が呼び出し側へ移り結果は破棄しない()
    {
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), new RecordingStyle());
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);
        VectorViewResult result = await renderer.RenderViewAsync(new VectorViewRequest(latitude, longitude, 14, 64, 64));

        using SKImage taken = result.TakeImage();
        result.Dispose();

        Assert.NotEqual(IntPtr.Zero, taken.Handle);
        Assert.Equal(64, taken.Width);
        Assert.Throws<ObjectDisposedException>(() => result.TakeImage());
    }

    [Fact]
    public async Task 描画のたびに新しい画像を返し共有しない()
    {
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), new RecordingStyle());
        (double latitude, double longitude) = TileToLatLon(TileX + 0.5, TileY + 0.5, BaseZoom);
        var request = new VectorViewRequest(latitude, longitude, 14, 64, 64);

        using VectorViewResult first = await renderer.RenderViewAsync(request);
        using VectorViewResult second = await renderer.RenderViewAsync(request);

        Assert.NotSame(first.Image, second.Image);
        Assert.NotEqual(first.Image.Handle, second.Image.Handle);
    }

    [Theory]
    [InlineData(double.NaN, 64, 64)]
    [InlineData(double.PositiveInfinity, 64, 64)]
    [InlineData(-0.5, 64, 64)]
    [InlineData(14, 0, 64)]
    [InlineData(14, 64, 0)]
    public async Task 不正な要求は引数の例外にする(double zoom, int width, int height)
    {
        var renderer = new VectorMapRenderer(SingleTileSource(MvtTestTile.CreateWater()), new RecordingStyle());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => renderer.RenderViewAsync(new VectorViewRequest(35, 139, zoom, width, height)));
    }

    private static MockVectorTileSource SingleTileSource(byte[] tile)
        => new(BaseZoom, (_, x, y) => x == TileX && y == TileY ? tile : null);

    /// <summary>タイル座標（小数を含む）の位置の緯度経度。</summary>
    private static (double Latitude, double Longitude) TileToLatLon(double tileX, double tileY, int zoom)
    {
        double n = Math.Pow(2, zoom);
        double longitude = (tileX / n * 360.0) - 180.0;
        double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * tileY / n)))) * 180.0 / Math.PI;
        return (latitude, longitude);
    }
}
