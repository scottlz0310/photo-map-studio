using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// 写真フォルダから地図画像を一括生成する。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "DI コンテナーと App.Tests の差し替え契約として公開する。")]
public interface IBatchGenerationService
{
    /// <summary>
    /// 一括生成を実行する。
    /// </summary>
    /// <param name="settings">生成設定。</param>
    /// <param name="progress">ファイル単位の進捗通知。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>生成結果の集計。</returns>
    Task<BatchGenerationSummary> GenerateAsync(
        BatchGenerationSettings settings,
        IProgress<BatchGenerationProgress>? progress,
        CancellationToken cancellationToken);
}
