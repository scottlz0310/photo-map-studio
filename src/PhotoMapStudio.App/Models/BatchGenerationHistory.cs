using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Models;

/// <summary>実行単位の保存記録。例外メッセージ・GPS・タイルURLは含めない。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "履歴サービスと表示層の公開データ契約。")]
public sealed record BatchGenerationHistory(
    int SchemaVersion, Guid Id, DateTimeOffset StartedAtUtc, DateTimeOffset FinishedAtUtc,
    string InputFolderPath, int SuccessCount, int SkippedCount, int ErrorCount, int TotalCount,
    bool IsCancelled, bool IsStopped, TimeSpan Elapsed, IReadOnlyList<BatchGenerationIssue> Issues)
{
    /// <summary>成功分のうち欠落警告を含む画像数。</summary>
    public int WarningCount => this.Issues.Count(issue => issue.Status == BatchGenerationStatus.Warning);
}
