using AiUsageBar.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AiUsageBar.AvaloniaApp.Behaviors;

/// <summary>
/// Drives a single, shared card as the heatmap's tooltip: it shows instantly, follows
/// the cursor, and swaps its text as the pointer crosses from one day cell to the next —
/// instead of the per-cell <c>ToolTip.Tip</c> (which has a delay and flickers on every
/// cell boundary). Port of the WPF <c>HeatmapTooltip</c> behavior, using a Canvas-hosted
/// card instead of a Popup.
///
/// Wire it on the heatmap's ItemsControl:
///   <code>behaviors:HeatmapTooltip.IsEnabled="True"
///         behaviors:HeatmapTooltip.Card="{Binding #HeatTip}"</code>
/// where <c>HeatTip</c> is a Border (child = TextBlock) sitting in a Canvas overlay.
/// </summary>
public static class HeatmapTooltip
{
    // Gap between the card's bottom edge and the cursor (it sits centered above).
    private const double Gap = 6;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(HeatmapTooltip));

    public static void SetIsEnabled(Control o, bool value) => o.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(Control o) => o.GetValue(IsEnabledProperty);

    /// <summary>The card (a Border whose child is a TextBlock) this behavior positions and fills.</summary>
    public static readonly AttachedProperty<Border?> CardProperty =
        AvaloniaProperty.RegisterAttached<Control, Border?>("Card", typeof(HeatmapTooltip));

    public static void SetCard(Control o, Border? value) => o.SetValue(CardProperty, value);
    public static Border? GetCard(Control o) => o.GetValue(CardProperty);

    static HeatmapTooltip()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control owner, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            owner.PointerMoved += OnPointerMoved;
            owner.PointerExited += OnPointerExited;
        }
        else
        {
            owner.PointerMoved -= OnPointerMoved;
            owner.PointerExited -= OnPointerExited;
        }
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var owner = (Control)sender!;
        if (GetCard(owner) is not { Parent: Visual canvas } card) return;

        // Refresh the text only when over an actual day cell; in the gaps between cells
        // keep the last text so it doesn't flicker as the pointer crosses.
        if (FindCell(e.Source as Visual, owner) is not { } cell) return;

        if (card.Child is TextBlock text) text.Text = cell.Tooltip;

        var pos = e.GetPosition(canvas);
        Canvas.SetLeft(card, pos.X - card.Bounds.Width / 2);
        Canvas.SetTop(card, pos.Y - Gap - card.Bounds.Height);
        card.Opacity = 1;
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (GetCard((Control)sender!) is { } card) card.Opacity = 0;
    }

    /// <summary>Walk up from the hit element to the day cell (its DataContext) under the cursor.</summary>
    private static HeatCell? FindCell(Visual? src, Visual root)
    {
        while (src is not null && !ReferenceEquals(src, root))
        {
            if (src is Control { DataContext: HeatCell cell }) return cell;
            src = src.GetVisualParent();
        }
        return null;
    }
}
