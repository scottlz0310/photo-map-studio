using System.Diagnostics.CodeAnalysis;

using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// プレビュー対象の列挙と地図画像生成を担う。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "DI と App.Tests の差し替え可能なサービス契約として公開する。")]
public interface IPreviewGenerationService
{
    /// <summary>
    /// GPS 情報を持つ写真を見つかった順に返す。
    /// </summary>
    /// <param name="folderPath">入力フォルダ。</param>
    /// <param name="includeSubfolders">下位フォルダも探索するかどうか。</param>
    /// <param name="progress">列挙とGPS確認の進捗。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>段階的に読み込めるプレビュー対象。</returns>
    IAsyncEnumerable<PreviewPhoto> LoadPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        IProgress<PreviewLoadProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 選択中の写真からプレビューを生成する。
    /// </summary>
    /// <param name="photo">プレビュー対象。</param>
    /// <param name="settings">生成設定。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>生成結果。</returns>
    Task<PreviewGenerationResult> GenerateAsync(
        PreviewPhoto? photo,
        PreviewGenerationSettings settings,
        CancellationToken cancellationToken);
}
