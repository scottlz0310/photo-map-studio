using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml.Controls;

namespace PhotoMapStudio.App.Views;

/// <summary>
/// 地図生成オプションの UI。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML から生成される UserControl のため public が必要。")]
public sealed partial class GenerationOptionsView : UserControl
{
    /// <summary>コンポーネントを構築する。</summary>
    public GenerationOptionsView()
    {
        this.InitializeComponent();
    }
}
