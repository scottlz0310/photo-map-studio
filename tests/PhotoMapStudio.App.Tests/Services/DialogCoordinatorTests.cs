using PhotoMapStudio.App.Services;

namespace PhotoMapStudio.App.Tests.Services;

public class DialogCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 表示中のダイアログが閉じるまで次の確認を待機する(bool failFirst)
    {
        var coordinator = new DialogCoordinator();
        var close = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> first = coordinator.ShowAsync(() => close.Task);
        Assert.True(coordinator.IsBusy);
        bool nextShown = false;
        Task<bool> second = coordinator.ShowAsync(() => { nextShown = true; return Task.FromResult(true); });
        Assert.False(nextShown); Assert.False(second.IsCompleted);
        if (failFirst)
        {
            close.SetException(new InvalidOperationException("表示失敗"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        }
        else
        {
            close.SetResult(true);
            Assert.True(await first.ConfigureAwait(true));
        }

        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(nextShown); Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task 確認の待機中にキャンセルすると表示せず既存ダイアログを保持する()
    {
        var coordinator = new DialogCoordinator();
        var close = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> first = coordinator.ShowAsync(() => close.Task);
        using var cancellation = new CancellationTokenSource();
        bool nextShown = false;
        Task<bool> second = coordinator.ShowAsync(() => { nextShown = true; return Task.FromResult(true); }, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(nextShown); Assert.True(coordinator.IsBusy); Assert.False(first.IsCompleted);
        close.SetResult(true);
        Assert.True(await first.ConfigureAwait(true)); Assert.False(coordinator.IsBusy);
        Assert.True(await coordinator.ShowAsync(() => Task.FromResult(true)));
    }
}
