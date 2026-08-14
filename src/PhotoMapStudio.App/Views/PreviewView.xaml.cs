using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.WindowsRuntime;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

using PhotoMapStudio.App.ViewModels;

using Windows.Storage.Streams;

namespace PhotoMapStudio.App.Views;

/// <summary>
/// プレビュー領域。
/// </summary>
[ExcludeFromCodeCoverage]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML から生成される UserControl のため public が必要。")]
public sealed partial class PreviewView : UserControl
{
    private PreviewViewModel? observedViewModel;
    private long imageVersion;

    /// <summary>コンポーネントを構築する。</summary>
    public PreviewView()
    {
        this.InitializeComponent();
        this.DataContextChanged += this.PreviewView_DataContextChanged;
    }

    private void PreviewView_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (this.observedViewModel is not null)
        {
            this.observedViewModel.PropertyChanged -= this.PreviewViewModel_PropertyChanged;
        }

        this.observedViewModel = this.DataContext as PreviewViewModel;
        if (this.observedViewModel is null)
        {
            this.PreviewImage.Source = null;
            return;
        }

        this.observedViewModel.PropertyChanged += this.PreviewViewModel_PropertyChanged;
        _ = this.UpdatePreviewImageAsync(this.observedViewModel);
    }

    private void PreviewViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(PreviewViewModel.PreviewImageBytes))
        {
            if (this.observedViewModel is not null)
            {
                _ = this.UpdatePreviewImageAsync(this.observedViewModel);
            }
        }
    }

    private async Task UpdatePreviewImageAsync(PreviewViewModel viewModel)
    {
        long version = ++this.imageVersion;
        ReadOnlyMemory<byte> imageBytes = viewModel.PreviewImageBytes;

        if (imageBytes.IsEmpty)
        {
            if (version == this.imageVersion && ReferenceEquals(this.observedViewModel, viewModel))
            {
                this.PreviewImage.Source = null;
            }

            return;
        }

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(imageBytes.ToArray().AsBuffer());
        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);

        if (version == this.imageVersion && ReferenceEquals(this.observedViewModel, viewModel))
        {
            this.PreviewImage.Source = bitmap;
        }
    }
}
