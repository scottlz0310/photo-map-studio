using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// タイルキャッシュの IO エラーをローカル診断ログへ記録する。
/// </summary>
internal static class TileCacheErrorReporting
{
    /// <summary>
    /// 診断シンクへ記録するハンドラーを作る。
    /// </summary>
    /// <param name="sink">記録先。</param>
    /// <returns>キャッシュへ渡すハンドラー。</returns>
    public static TileCacheErrorHandler CreateHandler(IErrorDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        return exception =>
        {
            try
            {
                sink.Record(ErrorDiagnosticStage.TileCache, 0, exception);
            }
            catch (IOException)
            {
                // 診断ログを保存できなくても、キャッシュの読み書きは続ける（診断はあくまで補助）
            }
        };
    }
}
