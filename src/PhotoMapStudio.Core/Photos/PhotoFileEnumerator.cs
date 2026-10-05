namespace PhotoMapStudio.Core.Photos;

/// <summary>
/// 写真ファイルを決定的な相対パス順で列挙する <see cref="IPhotoFileEnumerator"/> の実装。
/// </summary>
public sealed class PhotoFileEnumerator(Func<string, IEnumerable<FileSystemInfo>>? readFolder = null) : IPhotoFileEnumerator
{
    private readonly Func<string, IEnumerable<FileSystemInfo>> readFolder = readFolder ?? (folder => new DirectoryInfo(folder).EnumerateFileSystemInfos());
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".tif", ".tiff", ".heic"];

    /// <inheritdoc />
    public IReadOnlyList<string> Enumerate(string folderPath, bool includeSubfolders = false,
        IProgress<PhotoEnumerationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderPath);

        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        string root = Path.GetFullPath(folderPath);
        var pending = new Stack<string>();
        var files = new List<string>();
        int folderCount = 0;
        pending.Push(root);
        while (pending.TryPop(out string? current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            folderCount++;
            try
            {
                foreach (FileSystemInfo entry in this.readFolder(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes = entry.Attributes;
                    // クラウド同期のプレースホルダーもReparsePointなので、リンク先のある項目だけ除外する。
                    if ((attributes & FileAttributes.System) != 0
                        || IsFileSystemLink(attributes, (attributes & FileAttributes.ReparsePoint) != 0 ? entry.LinkTarget : null)
                        || string.Equals(entry.Name, "$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (entry is DirectoryInfo)
                    {
                        if (includeSubfolders)
                        {
                            pending.Push(entry.FullName);
                        }
                    }
                    else if (SupportedExtensions.Contains(entry.Extension, StringComparer.OrdinalIgnoreCase))
                    {
                        files.Add(entry.FullName);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                progress?.Report(new(folderCount, files.Count, Path.GetRelativePath(root, current), exception.Message) { Failure = exception });
            }

            progress?.Report(new(folderCount, files.Count, Path.GetRelativePath(root, current)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        files.Sort((left, right) => StringComparer.Ordinal.Compare(Path.GetRelativePath(root, left), Path.GetRelativePath(root, right)));
        return files;
    }

    internal static bool IsFileSystemLink(FileAttributes attributes, string? linkTarget)
        => (attributes & FileAttributes.ReparsePoint) != 0 && linkTarget is not null;
}
