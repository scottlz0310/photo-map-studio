using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Maps;

public sealed class SkiaMapImageComposerTests : IDisposable
{
    private static readonly SKColor TileColor = new(0x30, 0x60, 0x90);
    private static readonly GeoCoordinate Tokyo = new(35.681166, 139.767111);

    private readonly string root = Path.Combine(Path.GetTempPath(), $"pms-map-{Guid.NewGuid():N}");

    public SkiaMapImageComposerTests() => Directory.CreateDirectory(this.root);

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    [Theory]
    [InlineData(800, 600)]
    [InlineData(100, 100)]
    [InlineData(256, 256)]
    [InlineData(1, 1)]
    public async Task 指定したサイズのPNGを出力する(int width, int height)
    {
        byte[] png = await ComposeAsync(new SolidTileProvider(TileColor), width, height);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
    }

    [Fact]
    public async Task 必要なタイルをすべて取得する()
    {
        var provider = new SolidTileProvider(TileColor);

        await ComposeAsync(provider, 800, 600);

        TileRange range = TileRange.Compute(WebMercator.ToTilePoint(Tokyo, 15), 800, 600);
        Assert.Equal(range.TileCountX * range.TileCountY, provider.RequestedTiles.Count);
        Assert.Equal(provider.RequestedTiles.Count, provider.RequestedTiles.Distinct().Count());
    }

    [Fact]
    public async Task ピン画像のアンカーは下端中央である()
    {
        const int Width = 200;
        const int Height = 200;
        string pinPath = this.CreatePinImage(width: 8, height: 16, SKColors.Magenta);

        byte[] png = await ComposeAsync(new SolidTileProvider(TileColor), Width, Height, pinPath);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        int centerX = Width / 2;
        int centerY = Height / 2;

        // 先端（下端中央）が撮影地点を指す: 中心の直上はピン、直下は地図のまま
        Assert.Equal(SKColors.Magenta, bitmap.GetPixel(centerX, centerY - 1));
        Assert.Equal(SKColors.Magenta, bitmap.GetPixel(centerX, centerY - 16));
        Assert.Equal(TileColor, bitmap.GetPixel(centerX, centerY));
        Assert.Equal(TileColor, bitmap.GetPixel(centerX, centerY - 17));
        Assert.Equal(TileColor, bitmap.GetPixel(centerX - 5, centerY - 8));
        Assert.Equal(TileColor, bitmap.GetPixel(centerX + 4, centerY - 8));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing.png")]
    public async Task ピン画像が使えない場合は代替描画へ切り替える(string? pinFileName)
    {
        const int Width = 200;
        const int Height = 200;
        string? pinPath = pinFileName is null ? null : Path.Combine(this.root, pinFileName);

        byte[] png = await ComposeAsync(new SolidTileProvider(TileColor), Width, Height, pinPath);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        int centerX = Width / 2;
        int centerY = Height / 2;

        // 同心円 3 枚: 中心は白、半径 10 の内側は赤、その外側の縁は白系、半径 12 の外はタイル
        Assert.Equal(SKColors.White, bitmap.GetPixel(centerX, centerY));
        Assert.Equal(new SKColor(235, 59, 36), bitmap.GetPixel(centerX, centerY - 7));
        Assert.Equal(TileColor, bitmap.GetPixel(centerX, centerY - 14));

        // 縁はアンチエイリアスで混色するため、赤でもタイルでもない明色であることを見る
        SKColor edge = bitmap.GetPixel(centerX, centerY - 11);
        Assert.NotEqual(TileColor, edge);
        Assert.NotEqual(new SKColor(235, 59, 36), edge);
        Assert.True(edge.Red > 200, $"縁が白系ではありません: {edge}");
    }

    [Fact]
    public async Task 出典を右下に焼き込む()
    {
        const int Width = 400;
        const int Height = 300;

        byte[] png = await ComposeAsync(new SolidTileProvider(TileColor), Width, Height);

        using SKBitmap bitmap = SKBitmap.Decode(png);

        // 右下は半透明の下地で暗くなり、左下はタイルのまま
        Assert.NotEqual(TileColor, bitmap.GetPixel(Width - 3, Height - 3));
        Assert.Equal(TileColor, bitmap.GetPixel(3, Height - 3));
    }

    [Fact]
    public async Task 切り出し位置はタイル境界を正しく跨ぐ()
    {
        // タイル番号の偶奇で色を変え、境界がどこに来るかを検証する
        var provider = new CheckerTileProvider();
        TileRange range = TileRange.Compute(WebMercator.ToTilePoint(Tokyo, 15), 200, 200);

        byte[] png = await ComposeAsync(provider, 200, 200);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        int boundaryX = WebMercator.TileSize - range.CropLeft;
        Assert.InRange(boundaryX, 1, 199);
        Assert.Equal(CheckerTileProvider.ColorOf(range.MinX, range.MinY), bitmap.GetPixel(boundaryX - 1, 0));
        Assert.Equal(CheckerTileProvider.ColorOf(range.MinX + 1, range.MinY), bitmap.GetPixel(boundaryX, 0));
    }

    [Fact]
    public async Task タイル取得の失敗は代替タイルに置き換えず伝播する()
    {
        var provider = new FailingTileProvider(new TileFetchException("取得できません"));

        await Assert.ThrowsAsync<TileFetchException>(() => ComposeAsync(provider, 200, 200));
    }

    [Fact]
    public async Task 復号できないタイルは合成エラーとして伝播する()
    {
        var provider = new BrokenTileProvider();

        await Assert.ThrowsAsync<MapCompositionException>(() => ComposeAsync(provider, 200, 200));
    }

    [Fact]
    public async Task キャンセル済みトークンでは合成しない()
    {
        var provider = new SolidTileProvider(TileColor);
        var composer = new SkiaMapImageComposer(provider);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => composer.ComposeAsync(
            new MapCompositionRequest { Center = Tokyo, TileSource = TileSources.OpenStreetMap },
            cts.Token));
        Assert.Empty(provider.RequestedTiles);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, -1)]
    public async Task 出力サイズが正でなければ例外を投げる(int width, int height)
    {
        var composer = new SkiaMapImageComposer(new SolidTileProvider(TileColor));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => composer.ComposeAsync(
            new MapCompositionRequest
            {
                Center = Tokyo,
                TileSource = TileSources.OpenStreetMap,
                Width = width,
                Height = height,
            },
            CancellationToken.None));
    }

    private static Task<byte[]> ComposeAsync(
        ITileProvider provider,
        int width,
        int height,
        string? pinImagePath = null)
    {
        var composer = new SkiaMapImageComposer(provider);
        return composer.ComposeAsync(
            new MapCompositionRequest
            {
                Center = Tokyo,
                TileSource = TileSources.OpenStreetMap,
                Width = width,
                Height = height,
                Zoom = 15,
                PinImagePath = pinImagePath,
            },
            CancellationToken.None);
    }

    private static byte[] CreateSolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private string CreatePinImage(int width, int height, SKColor color)
    {
        string path = Path.Combine(this.root, "pin.png");
        File.WriteAllBytes(path, CreateSolidPng(width, height, color));
        return path;
    }

    private sealed class SolidTileProvider(SKColor color) : ITileProvider
    {
        private readonly byte[] tile = CreateSolidPng(WebMercator.TileSize, WebMercator.TileSize, color);

        public List<(int Zoom, int X, int Y)> RequestedTiles { get; } = [];

        public Task<byte[]> GetTileAsync(
            TileSource source,
            int zoom,
            int x,
            int y,
            CancellationToken cancellationToken)
        {
            this.RequestedTiles.Add((zoom, x, y));
            return Task.FromResult(this.tile);
        }
    }

    private sealed class CheckerTileProvider : ITileProvider
    {
        public static SKColor ColorOf(int x, int y)
            => ((x + y) % 2 == 0) ? new SKColor(0xFF, 0x00, 0x00) : new SKColor(0x00, 0xFF, 0x00);

        public Task<byte[]> GetTileAsync(
            TileSource source,
            int zoom,
            int x,
            int y,
            CancellationToken cancellationToken)
            => Task.FromResult(CreateSolidPng(WebMercator.TileSize, WebMercator.TileSize, ColorOf(x, y)));
    }

    private sealed class FailingTileProvider(Exception failure) : ITileProvider
    {
        public Task<byte[]> GetTileAsync(
            TileSource source,
            int zoom,
            int x,
            int y,
            CancellationToken cancellationToken)
            => Task.FromException<byte[]>(failure);
    }

    private sealed class BrokenTileProvider : ITileProvider
    {
        public Task<byte[]> GetTileAsync(
            TileSource source,
            int zoom,
            int x,
            int y,
            CancellationToken cancellationToken)
            => Task.FromResult<byte[]>([0x00, 0x01, 0x02]);
    }
}
