using System.Text;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// 起動引数からスイート連携用のフォルダ指定を抽出する。
/// </summary>
internal static class LaunchArgumentParser
{
    /// <summary>
    /// 起動引数を解析する。
    /// </summary>
    /// <param name="arguments">Windows の起動引数文字列。</param>
    /// <returns>解析結果。</returns>
    public static LaunchArguments Parse(string? arguments)
    {
        List<string> tokens = Tokenize(arguments ?? string.Empty);
        string? inputDirectoryPath = null;
        string? outputDirectoryPath = null;
        var errors = new List<string>();

        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (TryReadOption(token, "--input-dir", out string? inlineInput))
            {
                inputDirectoryPath = ReadValue(tokens, ref index, inlineInput, "--input-dir", errors);
            }
            else if (TryReadOption(token, "--output-dir", out string? inlineOutput))
            {
                outputDirectoryPath = ReadValue(tokens, ref index, inlineOutput, "--output-dir", errors);
            }
        }

        return new LaunchArguments(inputDirectoryPath, outputDirectoryPath, errors);
    }

    private static string? ReadValue(
        List<string> tokens,
        ref int index,
        string? inlineValue,
        string option,
        List<string> errors)
    {
        string? value = inlineValue;
        if (value is null && index + 1 < tokens.Count && !tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = tokens[++index];
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"引数 {option} にはフォルダパスが必要です。");
            return null;
        }

        return value;
    }

    private static bool TryReadOption(string token, string option, out string? inlineValue)
    {
        if (string.Equals(token, option, StringComparison.OrdinalIgnoreCase))
        {
            inlineValue = null;
            return true;
        }

        string prefix = option + "=";
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            inlineValue = token[prefix.Length..];
            return true;
        }

        inlineValue = null;
        return false;
    }

    private static List<string> Tokenize(string arguments)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        bool quoted = false;

        foreach (char character in arguments)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                AddToken(tokens, token);
            }
            else
            {
                token.Append(character);
            }
        }

        AddToken(tokens, token);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder token)
    {
        if (token.Length == 0)
        {
            return;
        }

        tokens.Add(token.ToString());
        token.Clear();
    }
}

/// <summary>
/// 起動引数の解析結果。
/// </summary>
internal sealed record LaunchArguments(
    string? InputDirectoryPath,
    string? OutputDirectoryPath,
    IReadOnlyList<string> Errors);
