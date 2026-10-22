namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// 撮影地点を示すピン・マーカーの形状種別。
/// </summary>
public enum PinKind
{
    /// <summary>同梱の緑色ピン（針型、標準）。</summary>
    GreenPin = 0,

    /// <summary>同梱の赤色ピン（針型）。</summary>
    RedPin = 1,

    /// <summary>同梱の青色ピン（針型）。</summary>
    BluePin = 2,

    /// <summary>同心円ターゲットマーク（丸形、中央基準）。</summary>
    TargetCircle = 3,

    /// <summary>十字線マーク（クロスヘア、中央基準）。</summary>
    Crosshair = 4,

    /// <summary>ユーザー指定のカスタム画像ファイル。</summary>
    CustomImage = 5,
}
