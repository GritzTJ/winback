using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WinBack.Core.Models;

namespace WinBack.App.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is Visibility.Collapsed;
}

[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is bool b && !b;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is bool b && !b;
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value != null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Retourne Visible si la valeur est null, Collapsed sinon.</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value == null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(BackupRunStatus), typeof(Brush))]
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is not BackupRunStatus status) return Brushes.Transparent;
        return status switch
        {
            BackupRunStatus.Success => new SolidColorBrush(Color.FromRgb(16, 124, 16)),
            BackupRunStatus.PartialSuccess => new SolidColorBrush(Color.FromRgb(216, 59, 1)),
            BackupRunStatus.Error => new SolidColorBrush(Color.FromRgb(197, 15, 31)),
            BackupRunStatus.Cancelled => new SolidColorBrush(Color.FromRgb(107, 107, 107)),
            BackupRunStatus.Running => new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            _ => Brushes.Transparent
        };
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(BackupStrategy), typeof(bool))]
public class StrategyToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is BackupStrategy strategy && p is string param &&
            Enum.TryParse<BackupStrategy>(param, out var target))
            return strategy == target;
        return false;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
    {
        if (value is true && p is string param &&
            Enum.TryParse<BackupStrategy>(param, out var target))
            return target;
        return Binding.DoNothing;
    }
}

[ValueConversion(typeof(int), typeof(double))]
public class PercentToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is int i ? (double)i : 0.0;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is double d ? (int)d : 0;
}

[ValueConversion(typeof(EntryAction), typeof(Brush))]
public class EntryActionToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return value is EntryAction action ? action switch
        {
            EntryAction.Added => new SolidColorBrush(Color.FromRgb(16, 124, 16)),
            EntryAction.Modified => new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            EntryAction.Deleted => new SolidColorBrush(Color.FromRgb(197, 15, 31)),
            EntryAction.Error => new SolidColorBrush(Color.FromRgb(216, 59, 1)),
            _ => Brushes.Gray
        } : Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(EntryAction), typeof(string))]
public class EntryActionToIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is EntryAction action ? action switch
        {
            EntryAction.Added => "+",
            EntryAction.Modified => "~",
            EntryAction.Deleted => "−",
            EntryAction.Error => "⚠",
            _ => "?"
        } : "?";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        throw new NotSupportedException();
}
