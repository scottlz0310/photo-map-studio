namespace PhotoMapStudio.Core.Photos;

/// <summary>
/// フォルダ直下の写真ファイルを列挙する <see cref="IPhotoFileEnumerator"/> の実装。
/// </summary>
public sealed class PhotoFileEnumerator : IPhotoFileEnumerator
{
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".tif", ".tiff", ".heic"];

    /// <inheritdoc />
    public IReadOnlyList<string> Enumerate(string folderPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderPath);

        var folder = new DirectoryInfo(folderPath);
        if (!folder.Exists)
        {
            return [];
        }

        // 一括生成の処理順を OS 依存にしないため、ファイル名の昇順に整列する
        return [.. folder
            .EnumerateFiles()
            .Where(file => SupportedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .Select(file => file.FullName)];
    }
}
