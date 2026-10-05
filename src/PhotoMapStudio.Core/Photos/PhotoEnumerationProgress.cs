namespace PhotoMapStudio.Core.Photos;

/// <summary>フォルダ走査の途中経過と、続行可能な読み取りエラー。</summary>
public sealed record PhotoEnumerationProgress(int FolderCount, int PhotoCount, string RelativePath, string? Error = null)
{
    /// <summary>画面の文言とは別に、ローカル診断へ渡す読み取り失敗の原因。</summary>
    public Exception? Failure { get; init; }
}
