using System.Diagnostics.CodeAnalysis;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 一括生成を開始できない事前検証エラー。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "ViewModel と App.Tests から扱う生成エラー契約として公開する。")]
public sealed class BatchGenerationException : Exception
{
    /// <summary>既定のコンストラクター。</summary>
    public BatchGenerationException()
    {
    }

    /// <summary>メッセージを指定して初期化する。</summary>
    /// <param name="message">エラーメッセージ。</param>
    public BatchGenerationException(string message)
        : base(message)
    {
    }

    /// <summary>メッセージと内部例外を指定して初期化する。</summary>
    /// <param name="message">エラーメッセージ。</param>
    /// <param name="innerException">原因となった例外。</param>
    public BatchGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
