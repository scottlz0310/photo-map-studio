using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.ViewModels;

/// <summary>履歴の日本語表示。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "XAMLと表示テストから参照する。")]
public sealed class BatchHistoryViewModel
{
    /// <summary>保存済みの結果から表示を作る。</summary>
    public BatchHistoryViewModel(BatchGenerationHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        this.History = history;
        this.Issues = history.Issues.Select(issue => new BatchIssueViewModel(issue)).ToArray();
    }

    internal BatchGenerationHistory History { get; }
    /// <summary>履歴の選択肢。</summary>
    public string DisplayName => $"{this.History.FinishedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss} — {this.OutcomeText} / スキップ {this.History.SkippedCount} / エラー {this.History.ErrorCount}";
    /// <summary>元の入力フォルダ。</summary>
    public string InputFolderPath => this.History.InputFolderPath;
    /// <summary>写真の集計。</summary>
    public string SummaryText => $"写真の集計（{this.OutcomeText}）: 成功 {this.History.SuccessCount} / スキップ {this.History.SkippedCount} / エラー {this.History.ErrorCount}、全 {this.History.TotalCount} 枚、経過 {this.History.Elapsed:c} / 問題 {this.Issues.Count} 件";
    /// <summary>処理済みの問題だけを表示する。</summary>
    public IReadOnlyList<BatchIssueViewModel> Issues { get; }
    /// <summary>問題がない実行の説明。</summary>
    public string EmptyMessage => this.Issues.Count == 0 ? "処理済み分にスキップ・エラーはありません。" : string.Empty;
    private string OutcomeText => this.History.IsCancelled ? "キャンセル" : this.History.IsStopped ? "中止" : "完了";
}

/// <summary>構造化された問題の表示。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "XAMLと表示テストから参照する。")]
public sealed class BatchIssueViewModel(BatchGenerationIssue issue)
{
    /// <summary>入力フォルダからの相対パス。</summary>
    public string RelativePath => issue.RelativePath;
    /// <summary>状態と対象種別。</summary>
    public string StatusText => $"{(issue.Status == BatchGenerationStatus.Skip ? "スキップ" : "エラー")} / {issue.Target switch { BatchIssueTarget.Photo => "写真", BatchIssueTarget.Folder => "フォルダ", _ => "一括処理" }}";
    /// <summary>処理理由と秘密情報を含まない失敗情報。</summary>
    public string ReasonText
    {
        get
        {
            string reason = issue.Reason switch
            {
                BatchIssueReason.MissingGps => "GPS情報がありません。",
                BatchIssueReason.TileNotFound => "タイルが見つかりません。配信範囲とGPS位置を確認してください。",
                BatchIssueReason.GpsReadFailed => "GPS情報の読み取りに失敗しました。",
                BatchIssueReason.MapGenerationFailed => "地図の生成に失敗しました。",
                BatchIssueReason.OutputSaveFailed => "生成画像の保存に失敗しました。",
                BatchIssueReason.EnumerationFailed => "フォルダの読み取りに失敗しました。",
                _ => "一括処理の開始・継続に失敗しました。",
            };
            if (issue.HttpStatusCode is int status) { reason += $" HTTP {status}"; }
            if (issue.ExceptionType is not null) { reason += $" / {issue.ExceptionType}"; }
            if (issue.HResult is int code) { reason += $" (0x{code.ToString("X8", CultureInfo.InvariantCulture)})"; }
            return reason;
        }
    }
}
