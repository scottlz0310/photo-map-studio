using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml;

using PhotoMapStudio.App.ViewModels;

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
    }

    public MainViewModel ViewModel { get; }

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
        this.windowManager.Dispose();
    }
}
