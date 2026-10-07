using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Models;

/// <summary>生成処理が確定した問題。表示用メッセージやURLは保持しない。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "生成サービスと履歴の公開データ契約。")]
public sealed record BatchGenerationIssue(
    int Index, string RelativePath, BatchIssueTarget Target,
    BatchGenerationStatus Status, BatchIssueReason Reason,
    int? HttpStatusCode = null, string? ExceptionType = null, int? HResult = null);

/// <summary>問題の対象。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "公開データ契約で使用する。")]
public enum BatchIssueTarget
{
    /// <summary>写真。</summary>
    Photo,
    /// <summary>列挙対象フォルダ。</summary>
    Folder,
    /// <summary>一括処理全体。</summary>
    Batch,
}

/// <summary>秘密情報を含まない、処理結果の理由。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "公開データ契約で使用する。")]
public enum BatchIssueReason
{
    /// <summary>GPSなし。</summary>
    MissingGps,
    /// <summary>タイルなし（404）。</summary>
    TileNotFound,
    /// <summary>GPS読み取り失敗。</summary>
    GpsReadFailed,
    /// <summary>地図生成失敗。</summary>
    MapGenerationFailed,
    /// <summary>出力保存失敗。</summary>
    OutputSaveFailed,
    /// <summary>フォルダ列挙失敗。</summary>
    EnumerationFailed,
    /// <summary>一括処理の開始・継続失敗。</summary>
    BatchFailed,
}
