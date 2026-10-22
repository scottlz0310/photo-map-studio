using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Maps;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 設定 UI で選択できるピン形状の選択肢。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML の選択項目と設定 ViewModel の公開バインディング型として使用する。")]
public sealed record PinKindChoice(PinKind Kind, string DisplayName)
{
    /// <summary>
    /// 利用可能なすべてのピン形状選択肢。
    /// </summary>
    public static readonly IReadOnlyList<PinKindChoice> All =
    [
        new(PinKind.GreenPin, "緑ピン (同梱)"),
        new(PinKind.RedPin, "赤ピン (同梱)"),
        new(PinKind.BluePin, "青ピン (同梱)"),
        new(PinKind.TargetCircle, "二重円ターゲット"),
        new(PinKind.Crosshair, "十字ターゲット (測量風)"),
        new(PinKind.CustomImage, "カスタム画像..."),
    ];

    /// <summary>
    /// 既定のピン形状選択肢。
    /// </summary>
    public static PinKindChoice Default => All[0];

    /// <summary>
    /// 指定した <see cref="PinKind"/> に対応する選択肢を取得する。
    /// </summary>
    public static PinKindChoice FromKind(PinKind kind)
        => All.FirstOrDefault(c => c.Kind == kind) ?? Default;
}
