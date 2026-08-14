namespace PhotoMapStudio.Core.Photos;

/// <summary>
/// EXIF の読み取りに失敗したことを表す例外。GPS 情報が存在しない場合とは区別する。
/// </summary>
public sealed class ExifGpsReadException : Exception
{
    /// <summary>既定のコンストラクター。</summary>
    public ExifGpsReadException()
    {
    }

    /// <summary>メッセージを指定して初期化する。</summary>
    /// <param name="message">例外メッセージ。</param>
    public ExifGpsReadException(string message)
        : base(message)
    {
    }

    /// <summary>メッセージと内部例外を指定して初期化する。</summary>
    /// <param name="message">例外メッセージ。</param>
    /// <param name="innerException">原因となった例外。</param>
    public ExifGpsReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>メッセージ・対象ファイル・内部例外を指定して初期化する。</summary>
    /// <param name="message">例外メッセージ。</param>
    /// <param name="filePath">読み取りに失敗したファイルのパス。</param>
    /// <param name="innerException">原因となった例外。</param>
    public ExifGpsReadException(string message, string filePath, Exception innerException)
        : base(message, innerException)
        => FilePath = filePath;

    /// <summary>読み取りに失敗したファイルのパス。</summary>
    public string? FilePath { get; }
}
