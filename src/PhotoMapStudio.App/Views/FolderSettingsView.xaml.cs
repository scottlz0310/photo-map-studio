using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoMapStudio.App.Views;

/// <summary>
/// 入出力フォルダ設定の UI。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML から生成される UserControl のため public が必要。")]
public sealed partial class FolderSettingsView : UserControl
{
    /// <summary>入力フォルダ選択要求。</summary>
    public event EventHandler? InputFolderBrowseRequested;

    /// <summary>出力フォルダ選択要求。</summary>
    public event EventHandler? OutputFolderBrowseRequested;

    /// <summary>コンポーネントを構築する。</summary>
    public FolderSettingsView()
    {
        this.InitializeComponent();
    }

    private void InputFolderBrowseButton_Click(object sender, RoutedEventArgs e)
        => this.InputFolderBrowseRequested?.Invoke(this, EventArgs.Empty);

    private void OutputFolderBrowseButton_Click(object sender, RoutedEventArgs e)
        => this.OutputFolderBrowseRequested?.Invoke(this, EventArgs.Empty);
}
