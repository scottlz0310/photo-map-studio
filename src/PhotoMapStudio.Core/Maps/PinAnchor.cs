namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// 地図上に配置するピン画像のアンカー位置（基準点）。
/// </summary>
public enum PinAnchor
{
    /// <summary>
    /// 画像の下端中央。針型・涙型ピンなど、下端の先端が撮影地点を指す場合に使用する。
    /// </summary>
    BottomCenter = 0,

    /// <summary>
    /// 画像の図心中央。丸形アイコン、十字線、シンボルなど、中心が撮影地点を指す場合に使用する。
    /// </summary>
    Center = 1,
}
