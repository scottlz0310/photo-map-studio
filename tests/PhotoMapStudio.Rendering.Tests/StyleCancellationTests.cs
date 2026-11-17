using PhotoMapStudio.Rendering.Tests.TestSupport;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests;

/// <summary>スタイルが、レイヤー・地物の単位でキャンセルを確認し、キャンセルされたら <see cref="OperationCanceledException"/> で終わることを確認する。</summary>
public class StyleCancellationTests
{
    private const int Zoom = 17;

    [Theory]
    [InlineData("polygons")]
    [InlineData("lines")]
    [InlineData("symbols")]
    [InlineData("roadNames")]
    public void キャンセル済みなら描画のパスは例外で終わる(string pass)
    {
        VectorTile tile = Decode(MvtTestTile.Combine(
            MvtTestTile.CreateWater(),
            MvtTestTile.CreateTransportation(("primary", [(0, 0), (4096, 4096)]), ("path", [(0, 4096), (4096, 0)])),
            MvtTestTile.CreateRoadLayer("transportation_name", ("primary", [(0, 0), (4096, 4096)]))));
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Render(style, pass, canvas, tile, cts.Token));
    }

    [Theory]
    [InlineData("polygons")]
    [InlineData("lines")]
    public void キャンセルされていなければ例外にならない(string pass)
    {
        VectorTile tile = Decode(MvtTestTile.Combine(
            MvtTestTile.CreateWater(),
            MvtTestTile.CreateTransportation(("primary", [(0, 0), (4096, 4096)]))));
        using var style = new OpenMapTilesStyle();
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);

        Render(style, pass, canvas, tile, CancellationToken.None);
    }

    private static void Render(OpenMapTilesStyle style, string pass, SKCanvas canvas, VectorTile tile, CancellationToken cancellationToken)
    {
        var grid = new LabelCollisionGrid();
        var viewport = new SKRect(0, 0, 64, 64);
        SKMatrix matrix = SKMatrix.CreateScale(64f / 4096f, 64f / 4096f);
        switch (pass)
        {
            case "polygons":
                style.RenderPolygons(canvas, tile, 64f / 4096f, Zoom, cancellationToken);
                break;
            case "lines":
                style.RenderLines(canvas, tile, 64f / 4096f, Zoom, cancellationToken);
                break;
            case "symbols":
                style.RenderSymbols(canvas, tile, matrix, Zoom, grid, viewport, cancellationToken);
                break;
            case "roadNames":
                style.RenderLineLabels(canvas, tile, matrix, Zoom, grid, viewport, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pass));
        }
    }

    private static VectorTile Decode(byte[] data)
    {
        Assert.True(VectorTileDecoder.TryDecode(data, out VectorTile tile));
        return tile;
    }
}
