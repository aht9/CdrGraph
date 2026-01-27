using System.Globalization;
using System.Windows.Data;

namespace CdrGraph.Desktop.Converters;

[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // اصلاح: پشتیبانی از bool? (Nullable) که در RadioButton استفاده می‌شود
        if (targetType != typeof(bool) && targetType != typeof(bool?))
            throw new InvalidOperationException("The target must be a boolean or nullable boolean");

        if (value is bool b)
        {
            return !b;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (targetType != typeof(bool) && targetType != typeof(bool?))
            throw new InvalidOperationException("The target must be a boolean or nullable boolean");

        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}