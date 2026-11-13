using System.Reflection;

namespace PhotoMapStudio.Tiles;

/// <summary>
/// タイル取得時に表明する User-Agent を一元管理する（NFR-NET-02）。
/// </summary>
internal static class UserAgentProvider
{
    private const string ProductName = "PhotoMapStudio";
    private const string ProjectUrl = "https://github.com/scottlz0310/photo-map-studio";

    /// <summary>
    /// タイル配信元へ表明する、このライブラリの既定の User-Agent。
    /// </summary>
    /// <remarks>
    /// 他のアプリがこのライブラリを使うときは、自分の名称と連絡先で表明するため、
    /// <see cref="HttpClient.DefaultRequestHeaders"/> の <c>UserAgent</c> を設定する（<c>AddHttpClient</c> の構成など）。
    /// 設定されていれば、この値は使われない。
    /// </remarks>
    public static string Value { get; } = Build();

    /// <summary>
    /// <paramref name="client"/> に User-Agent が設定されていないときだけ、既定の値を要求へ付ける。
    /// </summary>
    /// <remarks>
    /// 要求に明示した User-Agent は、クライアントの既定値より優先される。
    /// 既定値を常に付けると、利用側が設定した User-Agent が上書きされるため、設定の有無で分ける。
    /// </remarks>
    /// <param name="request">送信する要求。</param>
    /// <param name="client">要求を送るクライアント。</param>
    internal static void ApplyDefault(HttpRequestMessage request, HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.ParseAdd(Value);
        }
    }

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
