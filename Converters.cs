using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace SegmentEffectPlugin
{
    /// <summary>
    /// int値とboolを変換するコンバーター（RadioButton用）
    /// ConverterParameter に一致する値の場合 true を返す
    /// </summary>
    public class IntToBoolConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out int paramInt))
                return intValue == paramInt;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter is string paramStr && int.TryParse(paramStr, out int paramInt))
                return paramInt;
            return Binding.DoNothing;
        }

        public override object ProvideValue(IServiceProvider serviceProvider) => this;
    }

    /// <summary>
    /// bool → Visibility コンバーター
    /// </summary>
    public class BoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }
}
