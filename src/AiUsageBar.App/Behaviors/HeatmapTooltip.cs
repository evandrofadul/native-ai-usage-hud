using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AiUsageBar.App.ViewModels;

namespace AiUsageBar.App.Behaviors;

/// <summary>
/// Drives a single, shared popup as the heatmap's tooltip: it shows instantly,
/// follows the cursor, and swaps its text as the pointer crosses from one day cell
/// to the next — instead of the stock per-cell <see cref="System.Windows.Controls.ToolTip"/>,
/// which has a show delay and flickers (close/reopen) on every cell boundary.
///
/// Wire it on the heatmap's ItemsControl:
///   <code>behaviors:HeatmapTooltip.IsEnabled="True"
///         behaviors:HeatmapTooltip.TargetPopup="{Binding ElementName=HeatTip}"</code>
/// and bind the popup's text block to <c>behaviors:HeatmapTooltip.Text</c>.
/// </summary>
public static class HeatmapTooltip
{
    // Gap between the tooltip's bottom edge and the cursor (it sits centered above).
    private const double Gap = 4;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(HeatmapTooltip),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);

    /// <summary>The popup this behavior opens, positions, and fills with text.</summary>
    public static readonly DependencyProperty TargetPopupProperty =
        DependencyProperty.RegisterAttached(
            "TargetPopup", typeof(Popup), typeof(HeatmapTooltip));

    public static void SetTargetPopup(DependencyObject o, Popup value) => o.SetValue(TargetPopupProperty, value);
    public static Popup? GetTargetPopup(DependencyObject o) => (Popup?)o.GetValue(TargetPopupProperty);

    /// <summary>Current tooltip text; the popup's text block binds to this.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(HeatmapTooltip), new PropertyMetadata(""));

    public static void SetText(DependencyObject o, string value) => o.SetValue(TextProperty, value);
    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue)
        {
            fe.MouseMove += OnMouseMove;
            fe.MouseLeave += OnMouseLeave;
        }
        else
        {
            fe.MouseMove -= OnMouseMove;
            fe.MouseLeave -= OnMouseLeave;
        }
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        var owner = (FrameworkElement)sender;
        if (GetTargetPopup(owner) is not { } popup) return;

        // Center the popup horizontally on the cursor and float it just above (offsets
        // are relative to the placement target, which the XAML sets to this ItemsControl).
        // The card carries a uniform margin (room for its shadow), so account for it.
        var pos = e.GetPosition(owner);
        if (popup.Child is FrameworkElement card)
        {
            var m = card.Margin;
            popup.HorizontalOffset = pos.X - m.Left - card.ActualWidth / 2;
            popup.VerticalOffset = pos.Y - Gap - m.Top - card.ActualHeight;
        }

        // Refresh the text only when over an actual day cell; in the 2px gaps between
        // cells we keep the last text so it doesn't flicker as the pointer crosses.
        if (FindCell(e.OriginalSource as DependencyObject, owner) is { } cell)
        {
            SetText(popup, cell.Tooltip);
            popup.IsOpen = true;
        }
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (GetTargetPopup((FrameworkElement)sender) is { } popup)
            popup.IsOpen = false;
    }

    /// <summary>Walk up from the hit element to the day cell (its DataContext) under the cursor.</summary>
    private static HeatCell? FindCell(DependencyObject? src, DependencyObject root)
    {
        while (src is not null && src != root)
        {
            if (src is FrameworkElement { DataContext: HeatCell cell })
                return cell;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }
}
