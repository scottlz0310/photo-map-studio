using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 一括生成のファイル単位の処理結果。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML バインディングと App.Tests の進捗契約で使用する。")]
public enum BatchGenerationStatus
{
    /// <summary>生成に成功した。</summary>
    Success,

    /// <summary>対象外としてスキップした。</summary>
    Skip,

    /// <summary>エラーとして記録した。</summary>
    Error,

    /// <summary>キャンセルにより中断した。</summary>
    Cancelled,
}
