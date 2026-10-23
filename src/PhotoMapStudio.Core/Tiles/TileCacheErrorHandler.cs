namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイルキャッシュの読み書き・整理で起きた IO エラー（キャッシュは無視して処理を続けるもの）の通知先。
/// </summary>
/// <remarks>
/// 呼び出し元の処理を止めないため、ハンドラーは例外を送出しないこと。
/// </remarks>
/// <param name="exception">起きた IO エラー。</param>
public delegate void TileCacheErrorHandler(Exception exception);
