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
    /// <summary>列挙中または写真の処理開始通知。</summary>
    public bool IsActivity { get; init; }
    /// <summary>対象件数が未確定の列挙中。</summary>
    public bool IsEnumerating { get; init; }
    /// <summary>現在までの成功数。</summary>
    public int SuccessCount { get; init; }
    /// <summary>現在までのスキップ数。</summary>
    public int SkippedCount { get; init; }
    /// <summary>現在までのエラー数。</summary>
    public int ErrorCount { get; init; }
    /// <summary>開始からの経過時間。</summary>
    public TimeSpan Elapsed { get; init; }
    /// <summary>一括取得の統計。</summary>
    public PhotoMapStudio.Core.Tiles.TileFetchStatistics? Tiles { get; init; }

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
