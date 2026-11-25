using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

internal static class KeyboardTraversal
{
    // Walk the realized WPF tab sequence. Composite controls (lists, calendars,
    // grids) retain their own arrow-key navigation.
    internal static void CheckWindow(Window window, string page, Action<bool, string> check)
    {
        window.Activate();
        var accent = ((System.Windows.Media.SolidColorBrush)window.FindResource("Accent")).Color;
        var primary = Visuals(window).OfType<Button>().Where(b => b.IsVisible && b.IsEnabled &&
            b.Background is System.Windows.Media.SolidColorBrush brush && brush.Color == accent).ToArray();
        check(primary.Length <= 1, page + " has " + primary.Length + " enabled solid-blue primary buttons: " +
            string.Join(", ", primary.Select(b => b.Content)));
        var previous = Keyboard.FocusedElement;
        var stops = new HashSet<FrameworkElement>();
        window.MoveFocus(new(FocusNavigationDirection.First));
        while (Keyboard.FocusedElement is FrameworkElement focused && Window.GetWindow(focused) == window && stops.Add(focused))
        {
            if (focused.FocusVisualStyle is { } style)
            {
                var adorner = new Control { Style = style };
                adorner.ApplyTemplate();
                if (Visuals(adorner).OfType<Shape>().Any(s => s.StrokeDashArray?.Count > 0)) throw new InvalidOperationException(page + " has a dashed keyboard focus marker on " + focused.GetType().Name + " " + System.Windows.Automation.AutomationProperties.GetAutomationId(focused) + " " + (focused as ContentControl)?.Content);
            }
            if (!focused.MoveFocus(new(FocusNavigationDirection.Next))) break;
            if (ReferenceEquals(Keyboard.FocusedElement, focused))
                throw new InvalidOperationException(page + " keeps Tab on " + focused.GetType().Name);
        }
        check(stops.Count > 1, page + " traverses " + stops.Count + " distinct native keyboard stops without a dashed focus template");
        if (previous is UIElement { IsVisible: true } original && Window.GetWindow(original) == window) original.Focus();
    }

    static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var node in Visuals(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return node;
    }
}

internal static class NativeButtonLayout
{
    internal static void CheckCaption(Button button, Action<bool, string> check)
    {
        button.UpdateLayout();
        var size = new TextBlock
        {
            Text = (string)button.Content, FontFamily = button.FontFamily, FontSize = button.FontSize,
            FontWeight = button.FontWeight, FontStyle = button.FontStyle, FontStretch = button.FontStretch
        };
        size.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var available = button.ActualWidth - button.Padding.Left - button.Padding.Right - button.BorderThickness.Left - button.BorderThickness.Right;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(button).PixelsPerInchX;
        check(button.IsVisible && available + 1 >= size.DesiredSize.Width,
            $"{button.Content} full caption at {dpi} DPI, text {size.DesiredSize.Width:0.#} / available {available:0.#} DIP");
    }
}
