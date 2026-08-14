using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PhotoMapStudio.App.Converters;

/// <summary>
/// Boolean を <see cref="Visibility"/> へ変換する。
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "XAML リソースから生成されるため public が必要。")]
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException("Visibility から Boolean への逆変換はサポートしていません。");
}
