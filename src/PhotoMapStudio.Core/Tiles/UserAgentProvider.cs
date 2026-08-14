using System.Reflection;

namespace PhotoMapStudio.Core.Tiles;

/// <summary>
/// タイル取得時に表明する User-Agent を一元管理する（NFR-NET-02）。
/// </summary>
public static class UserAgentProvider
{
    private const string ProductName = "PhotoMapStudio";
    private const string ProjectUrl = "https://github.com/scottlz0310/photo-map-studio";

    /// <summary>タイル配信元へ表明する User-Agent。</summary>
    public static string Value { get; } = Build();

    private static string Build()
    {
        Assembly assembly = typeof(UserAgentProvider).Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // SemVer の build metadata（+<commit sha> 等）は User-Agent には含めない
        int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        return $"{ProductName}/{version} (+{ProjectUrl})";
    }
}
