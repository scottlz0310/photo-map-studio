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
    bool IsCancelled)
{
    /// <summary>処理済みのエラー数。</summary>
    public int ErrorCount { get; init; }
    /// <summary>処理済みの写真数。</summary>
    public int ProcessedCount => this.SuccessCount + this.SkippedCount + this.ErrorCount;
    /// <summary>経過時間。</summary>
    public TimeSpan Elapsed { get; init; }
    /// <summary>タイル取得統計。</summary>
    public PhotoMapStudio.Core.Tiles.TileFetchStatistics? Tiles { get; init; }
    /// <summary>配信元の連続失敗による中止理由。</summary>
    public string? StopReason { get; init; }
}
