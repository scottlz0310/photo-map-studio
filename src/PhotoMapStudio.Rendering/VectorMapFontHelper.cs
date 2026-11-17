using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

using SkiaSharp;

namespace PhotoMapStudio.Rendering;

/// <summary>
/// 日本語を表示できる書体（Typeface）を選ぶ補助。ベクター地図の注記と、地図に重ねる出典・警告文の描画が共有する。
/// </summary>
/// <remarks>
/// 書体はシステムにあるものを使い、同梱しない。Yu Gothic UI・Meiryo・Hiragino Sans・Noto Sans CJK JP・MS Gothic の順に探し、
/// 見つからなければ、日本語の文字（「日」）を描ける書体をシステムから探し、それも無ければ既定の書体を返す。
/// 日本語の書体が無い環境では、日本語の注記が描かれない（文字化けや空白になる）ことがある。
/// </remarks>
public static class VectorMapFontHelper
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
