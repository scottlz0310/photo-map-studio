namespace PhotoMapStudio.App.Services;

/// <summary>
/// アプリケーション設定を保存するキー・バリュー ストア。
/// </summary>
internal interface ISettingsValueStore
{
    /// <summary>値を読み出す。</summary>
    /// <param name="key">キー。</param>
    /// <returns>保存値。存在しない場合は <see langword="null"/>。</returns>
    object? Read(string key);

    /// <summary>値を書き込む。</summary>
    /// <param name="key">キー。</param>
    /// <param name="value">値。</param>
    void Write(string key, object value);

    /// <summary>値を削除する。</summary>
    /// <param name="key">キー。</param>
    void Remove(string key);
}
