using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// <see cref="VectorMapRenderer.RenderViewAsync"/> の結果。描いた画像と、欠落したタイルの集計を持つ。
/// </summary>
/// <remarks>
/// 画像は描画のたびに新しく作られ、他と共有されない。この結果が所有しており、<see cref="Dispose"/> で破棄する。
/// 呼び出し側が画像の所有権を引き取る場合は <see cref="TakeImage"/> を使う（以後、この結果は画像を破棄しない）。
/// </remarks>
public sealed class VectorViewResult : IDisposable
{
    private SKImage? image;

    /// <summary>結果を作る。<see cref="IVectorMapRenderer"/> の実装（テスト用の偽物を含む）が使う。画像の所有権は、この結果へ移る。</summary>
    /// <param name="image">描いた画像。この結果が所有し、<see cref="Dispose"/> で破棄する。</param>
    /// <param name="requestedTileCount">要求したタイルの数。</param>
    /// <param name="unavailableTileCount">通信の失敗または停止で取得できなかったタイルの数。</param>
    /// <param name="absentTileCount">配信範囲外のタイルの数。</param>
    public VectorViewResult(SKImage image, int requestedTileCount = 0, int unavailableTileCount = 0, int absentTileCount = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedTileCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unavailableTileCount);
        ArgumentOutOfRangeException.ThrowIfNegative(absentTileCount);
        this.image = image;
        this.RequestedTileCount = requestedTileCount;
        this.UnavailableTileCount = unavailableTileCount;
        this.AbsentTileCount = absentTileCount;
    }

    /// <summary>描いた画像。この結果が所有する。破棄済み、または <see cref="TakeImage"/> の後は例外になる。</summary>
    /// <exception cref="ObjectDisposedException">画像を破棄した、または取り出した後。</exception>
    public SKImage Image => this.image ?? throw new ObjectDisposedException(nameof(VectorViewResult));

    /// <summary>要求したタイルの数。</summary>
    public int RequestedTileCount { get; }

    /// <summary>通信の失敗または停止で取得できず、背景だけになったタイルの数。</summary>
    public int UnavailableTileCount { get; }

    /// <summary>配信範囲外（配信元にない）で、背景だけになったタイルの数。</summary>
    public int AbsentTileCount { get; }

    /// <summary>背景だけになった領域のタイルの数。</summary>
    public int MissingTileCount => this.UnavailableTileCount + this.AbsentTileCount;

    /// <summary>欠落したタイルがなく、画像が要求の範囲を全て描いている。</summary>
    public bool IsComplete => this.MissingTileCount == 0;

    /// <summary>画像の所有権を呼び出し側へ移す。以後、この結果の <see cref="Dispose"/> は画像を破棄しない。</summary>
    /// <returns>描いた画像。呼び出し側が破棄する。</returns>
    /// <exception cref="ObjectDisposedException">画像を破棄した、または取り出した後。</exception>
    public SKImage TakeImage()
    {
        SKImage taken = this.Image;
        this.image = null;
        return taken;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.image?.Dispose();
        this.image = null;
    }
}
