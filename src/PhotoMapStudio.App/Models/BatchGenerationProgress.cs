using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 一括生成のファイル単位の進捗。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML バインディングと App.Tests の進捗契約として公開する。")]
public sealed record BatchGenerationProgress(
    int Index,
    int Total,
    string FileName,
    BatchGenerationStatus Status,
    string Message)
{
    /// <summary>UI に表示するステータス名。</summary>
    public string StatusText => this.Status switch
    {
        BatchGenerationStatus.Success => "SUCCESS",
        BatchGenerationStatus.Skip => "SKIP",
        BatchGenerationStatus.Error => "ERROR",
        BatchGenerationStatus.Cancelled => "CANCELLED",
        _ => this.Status.ToString().ToUpperInvariant(),
    };
}
