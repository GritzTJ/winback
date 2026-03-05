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
    private static readonly Brush SuccessBrush = Freeze(new SolidColorBrush(Color.FromRgb(16, 124, 16)));
    private static readonly Brush PartialBrush = Freeze(new SolidColorBrush(Color.FromRgb(216, 59, 1)));
    private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(197, 15, 31)));
    private static readonly Brush CancelledBrush = Freeze(new SolidColorBrush(Color.FromRgb(107, 107, 107)));
    private static readonly Brush InterruptedBrush = Freeze(new SolidColorBrush(Color.FromRgb(202, 80, 16)));
    private static readonly Brush RunningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0, 120, 212)));

    private static Brush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is not BackupRunStatus status) return Brushes.Transparent;
        return status switch
        {
            BackupRunStatus.Success => SuccessBrush,
            BackupRunStatus.PartialSuccess => PartialBrush,
            BackupRunStatus.Error => ErrorBrush,
            BackupRunStatus.Cancelled => CancelledBrush,
            BackupRunStatus.Interrupted => InterruptedBrush,
            BackupRunStatus.Running => RunningBrush,
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
    private static readonly Brush AddedBrush = Freeze(new SolidColorBrush(Color.FromRgb(16, 124, 16)));
    private static readonly Brush ModifiedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0, 120, 212)));
    private static readonly Brush DeletedBrush = Freeze(new SolidColorBrush(Color.FromRgb(197, 15, 31)));
    private static readonly Brush ActionErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(216, 59, 1)));

    private static Brush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return value is EntryAction action ? action switch
        {
            EntryAction.Added => AddedBrush,
            EntryAction.Modified => ModifiedBrush,
            EntryAction.Deleted => DeletedBrush,
            EntryAction.Error => ActionErrorBrush,
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
