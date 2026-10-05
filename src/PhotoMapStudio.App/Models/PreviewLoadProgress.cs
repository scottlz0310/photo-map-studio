using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Models;

/// <summary>プレビュー候補の走査・GPS確認の進捗。</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "サービスとViewModelで共有する進捗契約。")]
public sealed record PreviewLoadProgress(int CheckedCount, int TotalCount, int GpsCount, string Message, bool IsEnumerating = false, bool IsError = false);
