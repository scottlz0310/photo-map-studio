namespace PhotoMapStudio.App.Services;

/// <summary>呼び出し元で通知する。UIへの移送は受け手のProgressに任せる。</summary>
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
