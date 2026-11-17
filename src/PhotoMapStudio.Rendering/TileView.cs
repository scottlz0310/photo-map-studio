using System.Runtime.CompilerServices;

using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// レイヤーの地物ごとの外接矩形（タイル座標）。タイルのデコード済みモデルと同じ寿命で、1 回だけ計算して共有する。
/// 描画先の範囲と交わらない地物を、パスを作る前に除外するために使う。
/// </summary>
internal sealed class LayerBounds
{
    private static readonly ConditionalWeakTable<VectorTileLayer, LayerBounds> Cache = new();

    private readonly float[] left;
    private readonly float[] top;
    private readonly float[] right;
    private readonly float[] bottom;

    private LayerBounds(VectorTileLayer layer)
    {
        int count = layer.Features.Count;
        this.left = new float[count];
        this.top = new float[count];
        this.right = new float[count];
        this.bottom = new float[count];
        for (int i = 0; i < count; i++)
        {
            float l = float.MaxValue;
            float t = float.MaxValue;
            float r = float.MinValue;
            float b = float.MinValue;
            IReadOnlyList<IReadOnlyList<VectorPoint>> parts = layer.Features[i].GeometryParts;
            for (int p = 0; p < parts.Count; p++)
            {
                IReadOnlyList<VectorPoint> part = parts[p];
                for (int k = 0; k < part.Count; k++)
                {
                    VectorPoint point = part[k];
                    l = Math.Min(l, point.X);
                    t = Math.Min(t, point.Y);
                    r = Math.Max(r, point.X);
                    b = Math.Max(b, point.Y);
                }
            }

            this.left[i] = l;
            this.top[i] = t;
            this.right[i] = r;
            this.bottom[i] = b;
        }
    }

    /// <summary>レイヤーの外接矩形を取得する（初回だけ計算する）。複数スレッドから同時に呼んでよい。</summary>
    public static LayerBounds For(VectorTileLayer layer) => Cache.GetValue(layer, static l => new LayerBounds(l));

    /// <summary>地物が、余白（<paramref name="margin"/>）を含めて <paramref name="area"/> と交わるか。点を持たない地物は交わらない。</summary>
    public bool Intersects(int index, SKRect area, float margin)
        => !(this.right[index] + margin < area.Left
            || this.left[index] - margin > area.Right
            || this.bottom[index] + margin < area.Top
            || this.top[index] - margin > area.Bottom);

    /// <summary>地物の外接矩形。</summary>
    public SKRect Of(int index) => new(this.left[index], this.top[index], this.right[index], this.bottom[index]);
}

/// <summary>
/// 1 回のスタイル描画の「見える範囲」（タイル座標）とキャンセルトークン。
/// </summary>
/// <param name="Clip">描画先キャンバスの範囲（タイル座標）。この外の地物は描かない。</param>
/// <param name="Margin">縁の半透明画素の分の余白（タイル座標）。</param>
/// <param name="CancellationToken">キャンセルトークン。</param>
internal readonly record struct TileView(SKRect Clip, float Margin, CancellationToken CancellationToken)
{
    /// <summary>線の描画が、線幅（と尖った接合部）で張り出す分を含めた余白。</summary>
    public float StrokeMargin(float strokeWidth)
        // 接合部の既定は miter（miter limit 4）で、半幅の 4 倍まで張り出し得る。
        => (strokeWidth * 2f) + this.Margin;

    /// <summary>地物が描画先の範囲と交わるか。</summary>
    public bool IsVisible(LayerBounds bounds, int index, float margin) => bounds.Intersects(index, this.Clip, margin);

    /// <summary>キャンセルされていれば <see cref="OperationCanceledException"/> を投げる。</summary>
    public void ThrowIfCancellationRequested() => this.CancellationToken.ThrowIfCancellationRequested();
}
