namespace PhotoMapStudio.Core.Photos;

/// <summary>フォルダ走査の途中経過と、続行可能な読み取りエラー。</summary>
public sealed record PhotoEnumerationProgress(int FolderCount, int PhotoCount, string RelativePath, string? Error = null);
