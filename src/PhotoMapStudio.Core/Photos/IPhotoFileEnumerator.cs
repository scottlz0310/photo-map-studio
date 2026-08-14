namespace PhotoMapStudio.Core.Photos;

/// <summary>
/// 入力フォルダから処理対象の写真ファイルを列挙する。
/// </summary>
public interface IPhotoFileEnumerator
{
    /// <summary>
    /// フォルダ直下の対応拡張子のファイルを、ファイル名の昇順で列挙する。
    /// </summary>
    /// <param name="folderPath">入力フォルダのパス。</param>
    /// <returns>写真ファイルの絶対パス。フォルダが存在しない場合は空。</returns>
    IReadOnlyList<string> Enumerate(string folderPath);
}
