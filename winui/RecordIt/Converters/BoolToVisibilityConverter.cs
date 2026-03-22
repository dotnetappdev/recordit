using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace RecordIt.Converters;

internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool boolValue;
        if (value is bool b)
        {
            boolValue = b;
        }
        else
        {
            boolValue = false;
        }
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}
