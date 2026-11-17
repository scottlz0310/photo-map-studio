using PhotoMapStudio.Tiles;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests.TestSupport;

/// <summary>テストで、整数ズーム・RGBA のビットマップとして結果を扱うための補助。<see cref="VectorMapRenderer.RenderViewAsync"/> を呼ぶ。</summary>
internal static class VectorMapRendererTestExtensions
{
    public static Task<SKBitmap> RenderMapAsync(
        this VectorMapRenderer renderer,
        double latitude,
        double longitude,
        double zoom,
        int width,
        int height,
        CancellationToken cancellationToken = default)
        => renderer.RenderMapAsync(latitude, longitude, zoom, width, height, session: null, tileSource: null, cancellationToken);

    public static async Task<SKBitmap> RenderMapAsync(
        this VectorMapRenderer renderer,
        double latitude,
        double longitude,
        double zoom,
        int width,
        int height,
        TileFetchSession? session,
        TileSource? tileSource,
        CancellationToken cancellationToken = default)
    {
        using VectorViewResult result = await renderer.RenderViewAsync(
            new VectorViewRequest(latitude, longitude, zoom, width, height) { Session = session, TileSource = tileSource, ColorType = SKColorType.Rgba8888 },
            cancellationToken).ConfigureAwait(false);
        return SKBitmap.FromImage(result.Image);
    }
}
