using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.Core.Maps;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 設定 UI で選択できるアンカー基準点の選択肢。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML の選択項目と設定 ViewModel の公開バインディング型として使用する。")]
public sealed record PinAnchorChoice(PinAnchor Anchor, string DisplayName)
{
    /// <summary>
    /// 利用可能なすべてのアンカー基準点選択肢。
    /// </summary>
    public static readonly IReadOnlyList<PinAnchorChoice> All =
    [
        new(PinAnchor.BottomCenter, "下端中央 (ピン先端が地点)"),
        new(PinAnchor.Center, "中央 (図心・ターゲット中心が地点)"),
    ];

    /// <summary>
    /// 既定のアンカー基準点選択肢。
    /// </summary>
    public static PinAnchorChoice Default => All[0];

    /// <summary>
    /// 指定した <see cref="PinAnchor"/> に対応する選択肢を取得する。
    /// </summary>
    public static PinAnchorChoice FromAnchor(PinAnchor anchor)
        => All.FirstOrDefault(c => c.Anchor == anchor) ?? Default;
}
