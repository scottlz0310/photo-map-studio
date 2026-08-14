using SkiaSharp;

namespace PhotoMapStudio.Core.Maps;

/// <summary>
/// 出典表示の行組み。指定幅に収まらない場合は折り返す（仕様書 §6.6）。
/// </summary>
public static class AttributionLayout
{
    /// <summary>
    /// 出典文字列を指定幅に収まる行へ分割する。
    /// </summary>
    /// <param name="font">描画に使う書体。</param>
    /// <param name="text">出典文字列。</param>
    /// <param name="maxWidth">1 行あたりの最大幅（ピクセル）。</param>
    /// <returns>分割した行。1 文字も収まらない場合は空。</returns>
    public static IReadOnlyList<string> Wrap(SKFont font, string text, float maxWidth)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || maxWidth <= 0f)
        {
            return [];
        }

        List<string> lines = [];
        ReadOnlySpan<char> remaining = text;

        while (!remaining.IsEmpty)
        {
            int length = BreakLength(font, remaining, maxWidth);
            if (length <= 0)
            {
                // 1 文字も収まらない極小サイズ。呼び出し元が焼き込みを省略する
                return [];
            }

            lines.Add(new string(remaining[..length]));
            remaining = remaining[length..];
        }

        return lines;
    }

    private static int BreakLength(SKFont font, ReadOnlySpan<char> text, float maxWidth)
    {
        int length = (int)font.BreakText(text, maxWidth);
        if (length >= text.Length)
        {
            return text.Length;
        }

        // サロゲートペアを分断しない
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return length;
    }
}
