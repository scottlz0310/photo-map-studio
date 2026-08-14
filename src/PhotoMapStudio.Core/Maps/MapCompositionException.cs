namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// 地図画像の合成に失敗したことを表す例外。
/// </summary>
public sealed class MapCompositionException : Exception
{
    /// <summary>既定のコンストラクター。</summary>
    public MapCompositionException()
    {
    }

    /// <summary>メッセージを指定して初期化する。</summary>
    /// <param name="message">例外メッセージ。</param>
    public MapCompositionException(string message)
        : base(message)
    {
    }

    /// <summary>メッセージと内部例外を指定して初期化する。</summary>
    /// <param name="message">例外メッセージ。</param>
    /// <param name="innerException">原因となった例外。</param>
    public MapCompositionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
