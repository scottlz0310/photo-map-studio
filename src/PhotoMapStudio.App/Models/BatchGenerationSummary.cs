using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 一括生成の集計結果。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "ViewModel と App.Tests の生成結果契約として公開する。")]
public sealed record BatchGenerationSummary(
    int SuccessCount,
    int SkippedCount,
    int TotalCount,
    bool IsCancelled);
