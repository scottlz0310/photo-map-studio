namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>1画像に限定した縮退描画の設定と欠落集計。他の画像と共有しない。</summary>
public sealed class VectorMapRenderContext(bool allowIncompleteMaps)
{
    /// <summary>通信失敗時に警告付きの縮退描画を許可する。</summary>
    public bool AllowIncompleteMaps { get; } = allowIncompleteMaps;
    /// <summary>要求対象タイル数。</summary>
    public int RequestedTileCount { get; internal set; }
    /// <summary>通信失敗または通信停止で取得できなかったタイル数。</summary>
    public int UnavailableTileCount { get; internal set; }
    /// <summary>配信範囲外のタイル数。</summary>
    public int AbsentTileCount { get; internal set; }
    /// <summary>背景だけになった領域のタイル数。</summary>
    public int MissingTileCount => this.UnavailableTileCount + this.AbsentTileCount;
}
