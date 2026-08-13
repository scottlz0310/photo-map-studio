using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml;

namespace PhotoMapStudio.App;

// App は WinUI の生成 Main から起動されるため public である必要があり、
// 型名も Application 派生の規約上 App から変えられない（名前空間との競合は不可避）。
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "WinUI の生成 Main から参照される")]
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "WinUI 3 の標準構成では App 型と .App 名前空間の併存が避けられない")]
[ExcludeFromCodeCoverage]
public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
