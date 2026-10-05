using PhotoMapStudio.App.Models;

namespace PhotoMapStudio.App.Services;

/// <summary>写真ごとの出力先と名前を、UI と生成で同じ規則により解決する。</summary>
internal static class OutputPathResolver
{
    public static void Validate(string folder, string prefix, string postfix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string pathPart = Path.IsPathFullyQualified(folder) ? folder[Path.GetPathRoot(folder)!.Length..] : folder;
        if (pathPart.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(part => part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new ArgumentException("出力フォルダに使用できない文字が含まれています。");
        }
        if (prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || postfix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("付加文字にパス区切りやファイル名に使用できない文字が含まれています。");
        }

        if (!Path.IsPathFullyQualified(folder))
        {
            if (Path.IsPathRooted(folder) || folder.Contains(':', StringComparison.Ordinal))
            {
                throw new ArgumentException("ルート相対・ドライブ相対の出力先は指定できません。");
            }

            string sample = Path.Combine(Path.GetTempPath(), "PhotoMapStudio-output-validation");
            _ = ResolveFolder(sample, folder);
            if (prefix.Length == 0 && postfix.Length == 0 && SamePath(sample, ResolveFolder(sample, folder)))
            {
                throw new ArgumentException("写真と同じフォルダへの出力では、先頭文字または末尾文字を指定してください。");
            }
        }
        else
        {
            _ = Path.GetFullPath(folder);
        }
    }

    public static string ResolveFolder(string photoFolder, string outputFolder)
    {
        if (Path.IsPathFullyQualified(outputFolder))
        {
            return Path.GetFullPath(outputFolder);
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(photoFolder));
        string resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputFolder, root));
        string rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!SamePath(root, resolved) && !resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("相対出力先は写真と同じフォルダまたは下位フォルダを指定してください。");
        }

        return resolved;
    }

    public static string Resolve(string photo, BatchGenerationSettings settings)
    {
        string name = $"{settings.OutputFilePrefix}{Path.GetFileNameWithoutExtension(photo)}{settings.OutputFilePostfix}.png";
        if (name.Length > 255)
        {
            throw new ArgumentException("出力ファイル名が255文字を超えています。");
        }

        string photoFolder = Path.GetDirectoryName(Path.GetFullPath(photo))!;
        string outputFolder = ResolveFolder(photoFolder, settings.OutputFolderPath);
        if (settings.OutputFilePrefix.Length == 0 && settings.OutputFilePostfix.Length == 0 && SamePath(photoFolder, outputFolder))
        {
            throw new ArgumentException("写真と同じフォルダへの出力では、先頭文字または末尾文字を指定してください。");
        }

        string output = Path.Combine(outputFolder, name);
        if (SamePath(output, Path.GetFullPath(photo)))
        {
            throw new ArgumentException("元写真と同じパスには出力できません。");
        }

        return output;
    }

    private static bool SamePath(string left, string right)
        => string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);
}
