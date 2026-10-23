namespace PhotoMapStudio.Core.Maps;

/// <summary>画像の欠落情報。写真・座標・配信URLは含めない。</summary>
public sealed record IncompleteMapWarning(int MissingTileCount, int RequestedTileCount, bool NetworkSuspended)
{
    /// <summary>画像と処理結果に表示する警告。</summary>
    public string Message => $"警告: 地図データ欠落（{this.MissingTileCount}/{this.RequestedTileCount}タイル）"
        + (this.NetworkSuspended ? "・通信停止中／キャッシュのみ" : string.Empty);
}
