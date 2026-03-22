using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace RecordIt.Converters;

internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool boolValue = value switch
        {
            bool b   => b,
            bool? nb => nb == true,
            _        => false,
        };
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}
