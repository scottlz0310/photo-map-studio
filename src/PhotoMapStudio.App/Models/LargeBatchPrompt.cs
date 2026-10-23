using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.App.Models;

/// <summary>
/// 大量の一括生成を開始する前に利用者へ確認する内容。配信元ごとに、確認の要否と実際の取得方式に合った文言を決める。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "画面側の確認ダイアログと一括生成サービスの公開コールバック型として使用する。")]
public sealed record LargeBatchPrompt(int Count, string Title, string Message, string ConfirmButtonText)
{
    /// <summary>確認を求める写真枚数の下限（これを超えると確認する）。</summary>
    public const int ConfirmationThreshold = 100;

    private const string GsiHost = "cyberjapandata.gsi.go.jp";

    /// <summary>
    /// 確認が必要かどうかを返す。
    /// </summary>
    /// <remarks>
    /// 地理院タイルは公的機関の配信で従来の間隔を維持する。この PC 上の配信元（ループバック・ローカルファイル）は
    /// 他者のサーバーではなく、許可を確認する相手がいない。それ以外は他者の配信元であり、一括取得の可否を確認する。
    /// </remarks>
    /// <param name="source">一括生成に使うタイルソース。</param>
    /// <param name="count">一括生成の対象枚数。</param>
    /// <returns>確認が必要な場合は <see langword="true"/>。</returns>
    public static bool IsRequired(TileSource source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool isGsi = string.Equals(source.BuildTileUri(0, 0, 0).Host, GsiHost, StringComparison.OrdinalIgnoreCase);
        return count > ConfirmationThreshold && !isGsi && !source.IsLoopback;
    }

    /// <summary>
    /// 配信元に応じた確認内容を作成する。
    /// </summary>
    /// <param name="source">一括生成に使うタイルソース。</param>
    /// <param name="count">一括生成の対象枚数。</param>
    /// <returns>確認ダイアログに表示する内容。</returns>
    public static LargeBatchPrompt Create(TileSource source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source == TileSources.OpenFreeMap)
        {
            return new LargeBatchPrompt(
                count,
                string.Create(CultureInfo.InvariantCulture, $"OpenFreeMap で{count}枚を一括生成"),
                "OpenFreeMap の利用規約は、まとまった取得を 1 回あたり 3,000 タイルまたは 50 MB 以内・並列 4 以内とし、"
                    + "それを超える場合は運営への連絡を求めています。既定のサイズでは 1 枚あたり 12〜20 タイル程度を、"
                    + "1 接続ずつ順に取得します（取得済みのタイルはキャッシュを使います）。規約を確認してから開始してください。",
                "規約を確認して開始");
        }

        if (source.VectorSource is not null)
        {
            return new LargeBatchPrompt(
                count,
                string.Create(CultureInfo.InvariantCulture, $"カスタム（ベクター）の配信元で{count}枚を一括生成"),
                "配信元が一括取得・画像保存を許可していることを確認してください。"
                    + "未取得のタイル（PMTiles は範囲取得）は、1 接続ずつ 125 ミリ秒以上の間隔で取得します。",
                "許可を確認して開始");
        }

        return new LargeBatchPrompt(
            count,
            string.Create(CultureInfo.InvariantCulture, $"カスタム配信元で{count}枚を一括生成"),
            "配信元が一括取得・画像保存を許可していることを確認してください。未取得のタイルは 1 接続・1 秒間隔で取得します。",
            "許可を確認して開始");
    }
}
