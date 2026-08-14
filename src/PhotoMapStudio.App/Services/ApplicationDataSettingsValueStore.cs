using System.Diagnostics.CodeAnalysis;

using Windows.Storage;

namespace PhotoMapStudio.App.Services;

/// <summary>
/// packaged アプリの <see cref="ApplicationData.Current.LocalSettings"/> を使う設定ストア。
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI コンテナーから生成される設定ストア。")]
internal sealed class ApplicationDataSettingsValueStore : ISettingsValueStore
{
    private readonly ApplicationDataContainer settings;

    /// <summary>
    /// 現在のユーザーのローカル設定を使用する。
    /// </summary>
    public ApplicationDataSettingsValueStore()
        : this(ApplicationData.Current.LocalSettings)
    {
    }

    /// <summary>
    /// 指定された設定コンテナーを使用する。
    /// </summary>
    /// <param name="settings">設定コンテナー。</param>
    public ApplicationDataSettingsValueStore(ApplicationDataContainer settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc/>
    public object? Read(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return this.settings.Values.TryGetValue(key, out object? value) ? value : null;
    }

    /// <inheritdoc/>
    public void Write(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        this.settings.Values[key] = value;
    }

    /// <inheritdoc/>
    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.settings.Values.Remove(key);
    }
}
