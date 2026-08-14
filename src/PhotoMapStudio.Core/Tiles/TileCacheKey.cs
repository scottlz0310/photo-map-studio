using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイルキャッシュのキーを算出する。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1054:URI パラメーターは文字列にしません",
    Justification = "URL テンプレートは {z}/{x}/{y} を含む置換前の文字列であり、Uri では表現できない。")]
public static class TileCacheKey
{
    private const int HashLength = 8;

    /// <summary>
    /// キャッシュファイル名を算出する。
    /// </summary>
    /// <param name="urlTemplate">置換前の URL テンプレート。</param>
    /// <param name="zoom">ズームレベル。</param>
    /// <param name="x">タイル番号 X。</param>
    /// <param name="y">タイル番号 Y。</param>
    /// <returns><c>{urlHash}_{z}_{x}_{y}.png</c> 形式のファイル名。</returns>
    public static string Create(string urlTemplate, int zoom, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(urlTemplate);

        // タイルソースを切り替えてもキャッシュが混ざらないよう、テンプレート全体をハッシュに含める
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(urlTemplate));
        string prefix = Convert.ToHexStringLower(hash)[..HashLength];

        return string.Create(CultureInfo.InvariantCulture, $"{prefix}_{zoom}_{x}_{y}.png");
    }
}
