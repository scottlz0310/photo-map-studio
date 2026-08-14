using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// プレビュー対象として選択できる写真。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML の選択項目と App.Tests のプレビュー契約として公開する。")]
public sealed record PreviewPhoto
{
    /// <summary>
    /// 写真を構築する。
    /// </summary>
    /// <param name="filePath">写真の絶対パス。</param>
    public PreviewPhoto(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.FilePath = filePath;
    }

    /// <summary>写真の絶対パス。</summary>
    public string FilePath { get; }

    /// <summary>選択 UI に表示するファイル名。</summary>
    public string DisplayName => Path.GetFileName(this.FilePath);
}
