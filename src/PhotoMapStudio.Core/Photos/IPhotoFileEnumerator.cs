namespace PhotoMapStudio.Core.Photos;

/// <summary>
/// 入力フォルダから処理対象の写真ファイルを列挙する。
/// </summary>
public interface IPhotoFileEnumerator
{
    /// <summary>
    /// 対応拡張子のファイルを、入力からの相対パスの序数昇順で列挙する。
    /// </summary>
    /// <param name="folderPath">入力フォルダのパス。</param>
    /// <param name="includeSubfolders">下位フォルダも走査する。</param>
    /// <param name="progress">フォルダ単位の進捗とアクセス失敗の通知先。</param>
    /// <param name="cancellationToken">走査中のキャンセルトークン。</param>
    /// <returns>写真ファイルの絶対パス。フォルダが存在しない場合は空。</returns>
    IReadOnlyList<string> Enumerate(string folderPath, bool includeSubfolders = false,
        IProgress<PhotoEnumerationProgress>? progress = null, CancellationToken cancellationToken = default);
}
