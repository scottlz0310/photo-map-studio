using PhotoMapStudio.Core.Maps;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// MSIX に同梱するアセットの実行時パスを解決する。
/// </summary>
internal static class PhotoMapAssetPaths
{
    private const string GreenPinFileName = "green_pin.png";
    private const string RedPinFileName = "red_pin.png";
    private const string BluePinFileName = "blue_pin.png";

    /// <summary>
    /// 設定が空の場合に使用する既定ピンのパス。
    /// </summary>
    public static string DefaultPinImagePath
        => GetBundledPinImagePath(PinKind.GreenPin);

    /// <summary>
    /// 同梱ピン画像のパスを取得する。
    /// </summary>
    public static string GetBundledPinImagePath(PinKind kind) => kind switch
    {
        PinKind.RedPin => Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins", RedPinFileName),
        PinKind.BluePin => Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins", BluePinFileName),
        _ => Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins", GreenPinFileName),
    };

    /// <summary>
    /// ピン種別とユーザー設定パスに応じて、使用すべきピン画像パスを解決する。
    /// </summary>
    public static string? ResolvePinImagePath(PinKind kind, string? customPath) => kind switch
    {
        PinKind.GreenPin or PinKind.RedPin or PinKind.BluePin => GetBundledPinImagePath(kind),
        PinKind.CustomImage => string.IsNullOrWhiteSpace(customPath) ? DefaultPinImagePath : customPath.Trim(),
        _ => null,
    };

    /// <summary>
    /// ユーザー設定を優先し、空の場合は同梱ピンを返す（後方互換用）。
    /// </summary>
    public static string ResolvePinImagePath(string? configuredPath)
        => ResolvePinImagePath(PinKind.CustomImage, configuredPath)!;
}
