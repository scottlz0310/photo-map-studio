using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using PhotoMapStudio.App.Services;
using PhotoMapStudio.App.ViewModels;
using PhotoMapStudio.App.Views;

using Windows.Storage;
using Windows.Storage.Pickers;

using WinUIEx;

namespace PhotoMapStudio.App;

[ExcludeFromCodeCoverage]
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "WinRT のフォルダーピッカーは UI スレッドへ復帰する必要がある。")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "MainWindow は DI コンテナーから生成される。")]
internal sealed partial class MainWindow : Window
{
    private readonly WindowManager windowManager;
    private ContentDialog? helpDialog;
    private readonly DialogCoordinator dialogs = new();

    public MainWindow(MainViewModel viewModel)
    {
        this.ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        this.windowManager = WindowManager.Get(this);
        this.windowManager.Width = 1280;
        this.windowManager.Height = 800;
        this.windowManager.MinWidth = 960;
        this.windowManager.MinHeight = 640;
        this.windowManager.PersistenceId = "PhotoMapStudio.MainWindow";

        this.FolderSettings.InputFolderBrowseRequested += this.InputFolderBrowseRequested;
        this.FolderSettings.OutputFolderBrowseRequested += this.OutputFolderBrowseRequested;
        this.Closed += this.MainWindow_Closed;
        this.ViewModel.ConfirmLargeBatchAsync = this.ConfirmLargeBatchAsync;
        this.RootLayout.Loaded += this.RootLayout_Loaded;
    }

    public MainViewModel ViewModel { get; }

    private async void RootLayout_Loaded(object sender, RoutedEventArgs args)
        => await this.ViewModel.LoadBatchHistoryCommand.ExecuteAsync(null);

    /// <summary>
    /// スイート連携から渡された起動引数を画面へ適用する。
    /// </summary>
    /// <param name="arguments">起動引数文字列。</param>
    internal void ApplyLaunchArguments(string? arguments)
        => this.ViewModel.ApplyLaunchArguments(LaunchArgumentParser.Parse(arguments));

    private async void InputFolderBrowseRequested(object? sender, EventArgs e)
    {
        StorageFolder? folder = await this.PickFolderAsync(PickerLocationId.PicturesLibrary);
        if (folder is not null)
        {
            this.ViewModel.InputFolderPath = folder.Path;
        }
    }

    private async void OutputFolderBrowseRequested(object? sender, EventArgs e)
    {
        StorageFolder? folder = await this.PickFolderAsync(PickerLocationId.DocumentsLibrary);
        if (folder is not null)
        {
            this.ViewModel.OutputFolderPath = folder.Path;
        }
    }

    private async void HelpButton_Click(object sender, RoutedEventArgs args) => await this.ToggleHelpAsync();

    private async void HelpAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await this.ToggleHelpAsync();
    }

    private async Task ToggleHelpAsync()
    {
        if (this.helpDialog is not null) { this.helpDialog.Hide(); return; }
        if (this.dialogs.IsBusy) { return; }
        this.helpDialog = new ContentDialog
        {
            XamlRoot = this.RootLayout.XamlRoot,
            Title = "PhotoMapStudioの使い方",
            Content = new HelpView { Width = Math.Min(800, this.RootLayout.ActualWidth - 100), Height = Math.Min(540, this.RootLayout.ActualHeight - 160) },
            CloseButtonText = "閉じる",
        };
        this.helpDialog.Resources["ContentDialogMaxWidth"] = 900d;
        var closeAccelerator = new KeyboardAccelerator { Key = Windows.System.VirtualKey.F1 };
        closeAccelerator.Invoked += (_, args) => { args.Handled = true; this.helpDialog?.Hide(); };
        this.helpDialog.KeyboardAccelerators.Add(closeAccelerator);
        try { await this.dialogs.ShowAsync(async () => await this.helpDialog.ShowAsync()); }
        finally { this.helpDialog = null; }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UIディスパッチャの例外を呼び出し側のTaskへ伝播させるため。")]
    private Task<bool> ConfirmLargeBatchAsync(int count, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!this.DispatcherQueue.TryEnqueue(async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.RootLayout.XamlRoot,
                Title = $"カスタム配信元で{count}枚を一括生成",
                Content = new InfoBar
                {
                    IsOpen = true,
                    IsClosable = false,
                    Severity = InfoBarSeverity.Warning,
                    Message = "配信元が一括取得・画像保存を許可していることを確認してください。未取得のタイルは1秒間隔で取得します。"
                },
                PrimaryButtonText = "許可を確認して開始",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
            };
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ContentDialogResult result = await this.dialogs.ShowAsync(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using CancellationTokenRegistration registration = cancellationToken.Register(() => this.DispatcherQueue.TryEnqueue(() => dialog.Hide()));
                    return await dialog.ShowAsync();
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(result == ContentDialogResult.Primary);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
        {
            completion.TrySetException(new InvalidOperationException("確認画面をUIスレッドに表示できません。"));
        }
        return completion.Task;
    }

    private async Task<StorageFolder?> PickFolderAsync(PickerLocationId suggestedStartLocation)
    {
        FolderPicker picker = this.CreateFolderPicker();
        picker.SuggestedStartLocation = suggestedStartLocation;
        picker.FileTypeFilter.Add("*");
        return await picker.PickSingleFolderAsync();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _ = this.ViewModel.TrySaveSettings();
        this.ViewModel.Preview?.Dispose();
        this.windowManager.Dispose();
    }
}
