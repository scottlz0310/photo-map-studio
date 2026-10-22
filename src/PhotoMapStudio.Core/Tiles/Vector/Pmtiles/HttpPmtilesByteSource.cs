using System.Net;
using System.Net.Http.Headers;

namespace PhotoMapStudio.Core.Tiles.Vector.Pmtiles;

/// <summary>
/// HTTP Range Request を使用してリモート PMTiles アーカイブからバイト範囲を読み取るバイトソース。
/// </summary>
public sealed class HttpPmtilesByteSource : IPmtilesByteSource
{
    private readonly HttpClient httpClient;
    private readonly Uri archiveUri;
    private readonly SemaphoreSlim throttle;
    private bool disposed;

    /// <summary>
    /// 指定された URL と HttpClient を使用してバイトソースを初期化します。
    /// </summary>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="archiveUri">PMTiles アーカイブの URL。</param>
    /// <param name="maxConcurrency">最大同時リクエスト数 (既定: 4)。</param>
    public HttpPmtilesByteSource(HttpClient httpClient, Uri archiveUri, int maxConcurrency = 4)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.archiveUri = archiveUri ?? throw new ArgumentNullException(nameof(archiveUri));
        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "最大同時リクエスト数は 1 以上である必要があります。");
        }

        this.throttle = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <inheritdoc/>
    public async Task<byte[]> ReadRangeAsync(long offset, int length, TileFetchSession? session, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        session?.ThrowIfStopped();

        await this.throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            session?.ThrowIfStopped();
            session?.RecordRequest();

            using var request = new HttpRequestMessage(HttpMethod.Get, this.archiveUri);
            request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);

            using var response = await this.httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            // 200 OK 全体応答は Range 非対応であり、巨大なアーカイブの全量ダウンロードにつながるため即座に拒否する
            if (response.StatusCode == HttpStatusCode.OK)
            {
                throw new TileFetchException(
                    $"PMTiles 配信元が HTTP Range (206 Partial Content) に対応しておらず、200 OK 全体応答を返しました (要求 offset: {offset}, length: {length})。",
                    this.archiveUri,
                    response.StatusCode,
                    null);
            }

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new TileFetchException(
                    $"PMTiles Range 取得失敗: {(int)response.StatusCode} {response.ReasonPhrase} (offset: {offset}, length: {length})",
                    this.archiveUri,
                    response.StatusCode,
                    null);
            }

            byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            if (content.Length != length)
            {
                throw new TileFetchException(
                    $"PMTiles Range 応答のバイト長が不正です (要求: {length} バイト, 受信: {content.Length} バイト)。",
                    this.archiveUri,
                    response.StatusCode,
                    null);
            }

            return content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TileFetchException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TileFetchException(
                $"PMTiles Range 取得中にエラーが発生しました: {ex.Message}",
                this.archiveUri,
                null,
                ex);
        }
        finally
        {
            this.throttle.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.throttle.Dispose();
            this.disposed = true;
        }
    }
}
