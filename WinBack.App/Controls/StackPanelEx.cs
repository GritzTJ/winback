using System.Windows;
using System.Windows.Controls;

namespace WinBack.App.Controls;

/// <summary>
/// Fournit la propriété attachée <c>Spacing</c> pour <see cref="StackPanel"/>,
/// équivalent WPF de la propriété Spacing de WinUI.
/// Insère un espace uniforme entre chaque enfant visible.
/// </summary>
public static class StackPanelEx
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.RegisterAttached(
            "Spacing",
            typeof(double),
            typeof(StackPanelEx),
            new PropertyMetadata(0.0, OnSpacingChanged));

    public static double GetSpacing(DependencyObject obj)
        => (double)obj.GetValue(SpacingProperty);

    public static void SetSpacing(DependencyObject obj, double value)
        => obj.SetValue(SpacingProperty, value);

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not StackPanel panel) return;

        // Appliquer immédiatement si déjà chargé, sinon attendre Loaded
        if (panel.IsLoaded)
            ApplySpacing(panel, (double)e.NewValue);
        else
        {
            panel.Loaded -= Panel_Loaded;
            panel.Loaded += Panel_Loaded;
        }
    }

    private static void Panel_Loaded(object sender, RoutedEventArgs e)
    {
        var panel = (StackPanel)sender;
        ApplySpacing(panel, GetSpacing(panel));
    }

    private static void ApplySpacing(StackPanel panel, double spacing)
    {
        bool horizontal = panel.Orientation == Orientation.Horizontal;

        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is not FrameworkElement el) continue;

            bool isLast = i == panel.Children.Count - 1;
            var m = el.Margin;

            el.Margin = horizontal
                ? new Thickness(m.Left, m.Top, isLast ? m.Right : spacing, m.Bottom)
                : new Thickness(m.Left, m.Top, m.Right, isLast ? m.Bottom : spacing);
        }
    }
}
