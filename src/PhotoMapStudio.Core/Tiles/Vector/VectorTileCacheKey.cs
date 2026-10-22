using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// ベクタータイルキャッシュのキー（ファイル名）を算出する。
/// </summary>
public static class VectorTileCacheKey
{
    private const int HashLength = 8;

    /// <summary>
    /// キャッシュファイル名を算出する。
    /// </summary>
    /// <param name="sourceId">タイルソース識別子（または URL テンプレート）。</param>
    /// <param name="zoom">ズームレベル。</param>
    /// <param name="x">タイル番号 X。</param>
    /// <param name="y">タイル番号 Y。</param>
    /// <returns><c>{prefix}_{zoom}_{x}_{y}.pbf</c> 形式のファイル名。</returns>
    public static string Create(string sourceId, int zoom, int x, int y)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        // ソース識別子のハッシュを含め、配信元が異なってもキャッシュが混ざらないようにする
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceId));
        string prefix = Convert.ToHexStringLower(hash)[..HashLength];

        return string.Create(CultureInfo.InvariantCulture, $"{prefix}_{zoom}_{x}_{y}.pbf");
    }
}
