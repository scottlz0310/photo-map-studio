using SkiaSharp;

namespace PhotoMapStudio.Core.Tiles.Vector;

/// <summary>
/// ベクター地図描画用のフォント生成ヘルパー。
/// </summary>
internal static class VectorMapFontHelper
{
    private static readonly string[] PreferredJapaneseFamilies =
    [
        "Yu Gothic UI",
        "Meiryo",
        "Hiragino Sans",
        "Noto Sans CJK JP",
        "MS Gothic",
    ];

    /// <summary>
    /// 日本語の表示に適した書体（Typeface）を生成して取得する。
    /// 呼び出し側で Dispose する必要があります。
    /// </summary>
    /// <returns>日本語グリフをサポートする <see cref="SKTypeface"/>。</returns>
    public static SKTypeface CreateJapaneseTypeface()
    {
        foreach (string family in PreferredJapaneseFamilies)
        {
            var tf = SKTypeface.FromFamilyName(family);
            if (tf is not null && string.Equals(tf.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                return tf;
            }

            tf?.Dispose();
        }

        return SKFontManager.Default.MatchCharacter('日') ?? SKTypeface.Default;
    }
}
