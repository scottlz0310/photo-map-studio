using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 地図注記（ラベル・シンボル）の重複配置を防ぐための AABB（軸平行バウンディングボックス）衝突判定マネージャー。
/// </summary>
public sealed class LabelCollisionGrid
{
    private readonly List<SKRect> placedBounds = [];
    private readonly List<(string Key, SKPoint Center)> placedNames = [];
    private readonly float padding;

    /// <summary>
    /// 衝突判定グリッドを初期化する。
    /// </summary>
    /// <param name="padding">ラベル周囲の余白ピクセル数（既定 2 ピクセル）。</param>
    public LabelCollisionGrid(float padding = 2f)
    {
        this.padding = padding;
    }

    /// <summary>
    /// 指定された矩形が既存の配置済みラベルや境界と衝突せず、配置可能かを判定し、可能であれば登録する。
    /// </summary>
    /// <param name="rect">配置を試みる矩形。</param>
    /// <param name="viewport">描画画面全体の矩形（画面外判定用）。</param>
    /// <returns>配置に成功した場合は <see langword="true"/>、衝突した場合は <see langword="false"/>。</returns>
    public bool TryPlace(SKRect rect, SKRect viewport)
        => this.TryPlace([rect], viewport);

    /// <summary>曲線注記の各部分を一括判定し、全て配置可能な場合だけ登録する。</summary>
    public bool TryPlace(IReadOnlyList<SKRect> bounds, SKRect viewport, string? key = null, float repeatDistance = 0f)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds.Count == 0)
        {
            return false;
        }

        SKRect envelope = bounds[0];
        foreach (SKRect rect in bounds)
        {
            if (rect.Left < viewport.Left || rect.Right > viewport.Right || rect.Top < viewport.Top || rect.Bottom > viewport.Bottom)
            {
                return false;
            }

            var paddedRect = rect;
            paddedRect.Inflate(this.padding, this.padding);
            if (this.placedBounds.Any(placed => placed.IntersectsWith(paddedRect)))
            {
                return false;
            }

            envelope = SKRect.Union(envelope, rect);
        }

        var center = new SKPoint(envelope.MidX, envelope.MidY);
        if (key is not null && this.placedNames.Any(placed => placed.Key == key && SKPoint.Distance(placed.Center, center) < repeatDistance))
        {
            return false;
        }

        this.placedBounds.AddRange(bounds);
        if (key is not null)
        {
            this.placedNames.Add((key, center));
        }

        return true;
    }

    /// <summary>
    /// 登録済みの矩形をすべてクリアする。
    /// </summary>
    public void Clear()
    {
        this.placedBounds.Clear();
        this.placedNames.Clear();
    }
}
