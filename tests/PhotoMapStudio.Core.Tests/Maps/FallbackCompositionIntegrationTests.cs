using System.Collections.Concurrent;
using System.Net;

using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Maps;
using PhotoMapStudio.Core.Tiles;

using SkiaSharp;

namespace PhotoMapStudio.Core.Tests.Maps;

/// <summary>
/// 実際の <see cref="SkiaMapImageComposer"/> を通して、配信範囲外の切り替えが成立することを確認する。
/// </summary>
public class FallbackCompositionIntegrationTests
{
    private static readonly GeoCoordinate Paris = new(48.858370, 2.294481);
    private static readonly GeoCoordinate Tokyo = new(35.681166, 139.767111);
    private static readonly SKColor OsmTileColor = new(0x20, 0x80, 0x40);

    [Fact]
    public async Task 日本国外の写真は代替ソースで生成される()
    {
        var client = new RangeAwareTileClient();
        FallbackMapImageComposer composer = CreateComposer(client);

        MapCompositionResult result = await composer.ComposeAsync(
            new MapCompositionRequest { Center = Paris, Width = 400, Height = 300 },
            CancellationToken.None);

        Assert.True(result.UsedFallback);
        Assert.Equal(TileSources.OpenStreetMap, result.TileSource);

        // 出力は代替ソースのタイルで構成されている
        using SKBitmap bitmap = SKBitmap.Decode(result.Png.Span);
        Assert.Equal(400, bitmap.Width);
        Assert.Equal(OsmTileColor, bitmap.GetPixel(4, 4));

        Assert.Contains(TileSources.GsiPale.UrlTemplate, client.RequestedTemplates);
        Assert.Contains(TileSources.OpenStreetMap.UrlTemplate, client.RequestedTemplates);
    }

    [Fact]
    public async Task 日本国内の写真は既定ソースのまま生成される()
    {
        var client = new RangeAwareTileClient();
        FallbackMapImageComposer composer = CreateComposer(client);

        MapCompositionResult result = await composer.ComposeAsync(
            new MapCompositionRequest { Center = Tokyo, Width = 400, Height = 300 },
            CancellationToken.None);

        Assert.False(result.UsedFallback);
        Assert.Equal(TileSources.GsiPale, result.TileSource);
        Assert.DoesNotContain(TileSources.OpenStreetMap.UrlTemplate, client.RequestedTemplates);
    }

    private static FallbackMapImageComposer CreateComposer(ITileClient client)
        => new FallbackMapImageComposer(
            new SkiaMapImageComposer(new TileProvider(client, new InMemoryTileCache())),
            TileSources.WorldwideFallback);

    /// <summary>
    /// 地理院タイルの配信範囲（日本周辺）を模擬し、範囲外では HTTP 404 を返すクライアント。
    /// </summary>
    private sealed class RangeAwareTileClient : ITileClient
    {
        private readonly byte[] gsiTile = CreateSolidPng(new SKColor(0xE0, 0xE0, 0xD0));
        private readonly byte[] osmTile = CreateSolidPng(OsmTileColor);

        public ConcurrentBag<string> RequestedTemplates { get; } = [];

        public Task<byte[]> GetTileAsync(
            TileSource source,
            int zoom,
            int x,
            int y,
            CancellationToken cancellationToken)
        {
            this.RequestedTemplates.Add(source.UrlTemplate);

            if (!source.UrlTemplate.Contains("gsi.go.jp", StringComparison.Ordinal))
            {
                return Task.FromResult(this.osmTile);
            }

            // 日本の経度帯（およそ 122〜154 度）に対応するタイル番号だけ配信する
            double west = LongitudeToTileX(122.0, zoom);
            double east = LongitudeToTileX(154.0, zoom);
            if (x >= west && x <= east)
            {
                return Task.FromResult(this.gsiTile);
            }

            return Task.FromException<byte[]>(new TileFetchException(
                "タイルの取得が HTTP 404 で失敗しました",
                source.BuildTileUri(zoom, x, y),
                HttpStatusCode.NotFound,
                null));
        }

        private static double LongitudeToTileX(double longitude, int zoom)
            => (longitude + 180.0) / 360.0 * Math.Pow(2.0, zoom);

        private static byte[] CreateSolidPng(SKColor color)
        {
            using var bitmap = new SKBitmap(WebMercator.TileSize, WebMercator.TileSize);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(color);
            }

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    private sealed class InMemoryTileCache : ITileCache
    {
        private readonly ConcurrentDictionary<string, byte[]> entries = new(StringComparer.Ordinal);

        public Task<byte[]?> TryReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(this.entries.TryGetValue(key, out byte[]? content) ? content : null);

        public Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
        {
            this.entries[key] = content;
            return Task.CompletedTask;
        }
    }
}
