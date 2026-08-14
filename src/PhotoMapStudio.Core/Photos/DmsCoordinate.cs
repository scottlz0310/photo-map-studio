using MetadataExtractor;

namespace PhotoMapStudio.Core.Photos;

/// <summary>
/// EXIF の度分秒（DMS）表現を十進度へ変換する。
/// </summary>
public static class DmsCoordinate
{
    /// <summary>
    /// 度分秒の有理数列を十進度へ変換する。
    /// </summary>
    /// <param name="dms">度・分・秒の 3 要素。要素数が 3 未満の場合は 0 を返す。</param>
    /// <param name="reference">方位（<c>N</c> / <c>S</c> / <c>E</c> / <c>W</c>）。<c>S</c> と <c>W</c> は符号を反転する。</param>
    /// <returns>十進度の値。</returns>
    public static double ToDecimalDegrees(IReadOnlyList<Rational>? dms, string? reference)
    {
        if (dms is null || dms.Count < 3)
        {
            return 0.0;
        }

        double degrees = ToDouble(dms[0]) + (ToDouble(dms[1]) / 60.0) + (ToDouble(dms[2]) / 3600.0);
        return IsNegativeReference(reference) ? -degrees : degrees;
    }

    // 分母 0 の有理数を含む破損 EXIF が実在するため、ゼロ除算を避けて分子を採用する
    private static double ToDouble(Rational value)
        => value.Denominator == 0 ? value.Numerator : value.Numerator / (double)value.Denominator;

    private static bool IsNegativeReference(string? reference)
    {
        string trimmed = reference?.Trim() ?? string.Empty;
        return trimmed.Equals("S", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("W", StringComparison.OrdinalIgnoreCase);
    }
}
