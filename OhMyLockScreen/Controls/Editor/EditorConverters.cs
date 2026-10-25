using System.Globalization;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Controls.Editor;

public sealed class FiniteNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is not double number || !double.IsFinite(number))
        {
            return DependencyProperty.UnsetValue;
        }

        return targetType == typeof(int) ? (object)(int)number : number;
    }
}

public sealed class EnumIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not Enum)
        {
            return -1;
        }

        return Array.IndexOf(Enum.GetValues(value.GetType()), value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (!targetType.IsEnum || value is not int index)
        {
            return DependencyProperty.UnsetValue;
        }

        var values = Enum.GetValues(targetType);
        return index >= 0 && index < values.Length ? values.GetValue(index)! : DependencyProperty.UnsetValue;
    }
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}

public sealed class BooleanVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (parameter is "Inverse")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}