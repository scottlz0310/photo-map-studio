namespace PhotoMapStudio.App.Services;

/// <summary>同じウィンドウ内のダイアログを直列に表示する。</summary>
internal sealed class DialogCoordinator
{
    private readonly Lock synchronization = new();
    private TaskCompletionSource? current;

    public bool IsBusy
    {
        get { lock (this.synchronization) { return this.current is not null; } }
    }

    public async Task<T> ShowAsync<T>(Func<Task<T>> show, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(show);
        TaskCompletionSource ownership;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task previous;
            lock (this.synchronization)
            {
                if (this.current is null)
                {
                    ownership = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    this.current = ownership;
                    break;
                }

                previous = this.current.Task;
            }

            await previous.WaitAsync(cancellationToken).ConfigureAwait(true);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await show().ConfigureAwait(true);
        }
        finally
        {
            lock (this.synchronization) { this.current = null; }
            ownership.SetResult();
        }
    }
}
