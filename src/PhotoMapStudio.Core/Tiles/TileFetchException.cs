using System.Net;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイル取得に失敗したことを表す例外。失敗を無言でグレータイルに置き換えない（仕様書 §9-2）。
/// </summary>
public sealed class TileFetchException : Exception
{
    /// <summary>既定のコンストラクター。</summary>
    public TileFetchException()
    {
    }

    /// <summary>メッセージを指定して初期化する。</summary>
    /// <param name="message">例外メッセージ。</param>
    public TileFetchException(string message)
        : base(message)
    {
    }

    /// <summary>メッセージと内部例外を指定して初期化する。</summary>
    /// <param name="message">例外メッセージ。</param>
    /// <param name="innerException">原因となった例外。</param>
    public TileFetchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>メッセージ・対象 URL・HTTP ステータス・内部例外を指定して初期化する。</summary>
    /// <param name="message">例外メッセージ。</param>
    /// <param name="requestUri">取得しようとしたタイルの URL。</param>
    /// <param name="statusCode">応答の HTTP ステータス。応答を得られなかった場合は <see langword="null"/>。</param>
    /// <param name="innerException">原因となった例外。応答を得た場合は <see langword="null"/>。</param>
    public TileFetchException(string message, Uri requestUri, HttpStatusCode? statusCode, Exception? innerException)
        : base(message, innerException)
    {
        this.RequestUri = requestUri;
        this.StatusCode = statusCode;
    }

    /// <summary>取得しようとしたタイルの URL。</summary>
    public Uri? RequestUri { get; }

    /// <summary>応答の HTTP ステータス。</summary>
    public HttpStatusCode? StatusCode { get; }
}
