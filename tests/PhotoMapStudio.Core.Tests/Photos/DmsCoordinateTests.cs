using MetadataExtractor;

using PhotoMapStudio.Core.Photos;

namespace PhotoMapStudio.Core.Tests.Photos;

public class DmsCoordinateTests
{
    [Theory]
    [InlineData(35, 1, 40, 1, 522, 10, "N", 35.6811666666667)]
    [InlineData(35, 1, 40, 1, 522, 10, "S", -35.6811666666667)]
    [InlineData(139, 1, 46, 1, 16, 10, "E", 139.7671111111111)]
    [InlineData(139, 1, 46, 1, 16, 10, "W", -139.7671111111111)]
    public void 度分秒を十進度へ変換する(
        long degreeNumerator,
        long degreeDenominator,
        long minuteNumerator,
        long minuteDenominator,
        long secondNumerator,
        long secondDenominator,
        string reference,
        double expected)
    {
        Rational[] dms =
        [
            new(degreeNumerator, degreeDenominator),
            new(minuteNumerator, minuteDenominator),
            new(secondNumerator, secondDenominator),
        ];

        double actual = DmsCoordinate.ToDecimalDegrees(dms, reference);

        Assert.Equal(expected, actual, 9);
    }

    [Theory]
    [InlineData("n", 35.0)]
    [InlineData("s", -35.0)]
    [InlineData(" S ", -35.0)]
    [InlineData(null, 35.0)]
    public void 方位は大文字小文字と前後の空白を無視する(string? reference, double expected)
    {
        Rational[] dms = [new(35, 1), new(0, 1), new(0, 1)];

        Assert.Equal(expected, DmsCoordinate.ToDecimalDegrees(dms, reference));
    }

    public static TheoryData<Rational[]?> 不足した要素列 =>
    [
        null,
        [],
        [new Rational(35, 1)],
        [new Rational(35, 1), new Rational(40, 1)],
    ];

    [Theory]
    [MemberData(nameof(不足した要素列))]
    public void 要素数が三未満なら零を返す(Rational[]? dms)
        => Assert.Equal(0.0, DmsCoordinate.ToDecimalDegrees(dms, "N"));

    [Theory]
    [InlineData(35, 0, 0, 1, 0, 1, 35.0)]
    [InlineData(0, 1, 30, 0, 0, 1, 0.5)]
    [InlineData(0, 1, 0, 1, 36, 0, 0.01)]
    public void 分母が零の要素は分子をそのまま採用する(
        long degreeNumerator,
        long degreeDenominator,
        long minuteNumerator,
        long minuteDenominator,
        long secondNumerator,
        long secondDenominator,
        double expected)
    {
        Rational[] dms =
        [
            new(degreeNumerator, degreeDenominator),
            new(minuteNumerator, minuteDenominator),
            new(secondNumerator, secondDenominator),
        ];

        Assert.Equal(expected, DmsCoordinate.ToDecimalDegrees(dms, "N"), 9);
    }
}
