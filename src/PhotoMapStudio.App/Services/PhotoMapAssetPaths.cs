namespace PhotoMapStudio.App.Services;

/// <summary>
/// MSIX に同梱するアセットの実行時パスを解決する。
/// </summary>
internal static class PhotoMapAssetPaths
{
    private const string DefaultPinFileName = "green_pin.png";

    /// <summary>
    /// 設定が空の場合に使用する既定ピンのパス。
    /// </summary>
    public static string DefaultPinImagePath
        => Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins", DefaultPinFileName);

    /// <summary>
    /// ユーザー設定を優先し、空の場合は同梱ピンを返す。
    /// </summary>
    public static string ResolvePinImagePath(string? configuredPath)
        => string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultPinImagePath
            : configuredPath.Trim();
}
