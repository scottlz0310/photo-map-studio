using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// 地図注記（ラベル・シンボル）の重複配置を防ぐための AABB（軸平行バウンディングボックス）衝突判定マネージャー。
/// </summary>
public sealed class LabelCollisionGrid
{
    private readonly List<SKRect> placedBounds = [];
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
    {
        // 画面外にはみ出している場合は配置しない
        if (rect.Left < viewport.Left || rect.Right > viewport.Right ||
            rect.Top < viewport.Top || rect.Bottom > viewport.Bottom)
        {
            return false;
        }

        var paddedRect = new SKRect(
            rect.Left - this.padding,
            rect.Top - this.padding,
            rect.Right + this.padding,
            rect.Bottom + this.padding);

        for (int i = 0; i < this.placedBounds.Count; i++)
        {
            if (this.placedBounds[i].IntersectsWith(paddedRect))
            {
                return false;
            }
        }

        this.placedBounds.Add(rect);
        return true;
    }

    /// <summary>
    /// 登録済みの矩形をすべてクリアする。
    /// </summary>
    public void Clear()
    {
        this.placedBounds.Clear();
    }
}
