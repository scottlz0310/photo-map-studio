using System.Net.Http.Headers;
using System.Text.RegularExpressions;

using PhotoMapStudio.Core.Tiles;

namespace PhotoMapStudio.Core.Tests.Tiles;

public class UserAgentProviderTests
{
    [Fact]
    public void アプリ名とバージョンと参照先を表明する()
        => Assert.Matches(
            new Regex(
                @"^PhotoMapStudio/\d+\.\d+\.\d+ \(\+https://github\.com/scottlz0310/photo-map-studio\)$",
                RegexOptions.None,
                TimeSpan.FromSeconds(1)),
            UserAgentProvider.Value);

    [Fact]
    public void ビルドメタデータを含まない()
        => Assert.DoesNotContain("+", UserAgentProvider.Value.Split(' ')[0], StringComparison.Ordinal);

    [Fact]
    public void HTTPヘッダーとして解釈できる()
        => Assert.True(ProductInfoHeaderValue.TryParse(UserAgentProvider.Value.Split(' ')[0], out _));
}
