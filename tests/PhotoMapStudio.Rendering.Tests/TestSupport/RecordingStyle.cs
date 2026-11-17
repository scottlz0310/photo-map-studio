using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering.Tests.TestSupport;

/// <summary>呼ばれた描画のパスと、渡されたズームを記録するスタイル。<see cref="OnCall"/> で、パスごとの処理を差し込める。</summary>
internal sealed class RecordingStyle : IVectorMapStyle
{
    public List<string> Calls { get; } = [];

    public List<int> Zooms { get; } = [];

    public Action<string>? OnCall { get; set; }

    public string StyleId => "recording";

    public SKColor BackgroundColor => SKColors.White;

    public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken) => this.Record("polygons", zoom);

    public void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom, CancellationToken cancellationToken) => this.Record("lines", zoom);

    public void RenderSymbols(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        => this.Record("symbols", zoom);

    public void RenderLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        => this.Record("labels", zoom);

    public void RenderLineLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport, CancellationToken cancellationToken)
        => this.Record("roadNames", zoom);

    private void Record(string pass, int zoom)
    {
        this.Calls.Add(pass);
        this.Zooms.Add(zoom);
        this.OnCall?.Invoke(pass);
    }
}
