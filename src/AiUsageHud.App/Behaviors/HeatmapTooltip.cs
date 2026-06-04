using AiUsageHud.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AiUsageHud.App.Behaviors;

/// <summary>
/// Drives a single, shared <see cref="Popup"/> as the heatmap's tooltip: it shows
/// instantly, follows the cursor, and swaps its text as the pointer crosses from one
/// day cell to the next — instead of the per-cell <c>ToolTip.Tip</c> (which has a delay
/// and flickers on every cell boundary).
///
/// The card lives in a <see cref="Popup"/> rather than the window's overlay layer so it
/// can render <em>outside</em> the (small, borderless) window — the OS hosts the popup as
/// its own top-level, clamped to the screen instead of the window, so the card is no
/// longer clipped by the window edge.
///
/// Wire it on the heatmap's ItemsControl:
///   <code>behaviors:HeatmapTooltip.IsEnabled="True"
///         behaviors:HeatmapTooltip.Card="{Binding #HeatTip}"</code>
/// where <c>HeatTip</c> is a Popup whose child is a Border (child = TextBlock).
/// </summary>
public static class HeatmapTooltip
{
    // Distance the card floats above the cursor. Also a buffer so the popup never sits
    // under the pointer (which would steal the hover and make it flicker).
    private const double Gap = 5;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(HeatmapTooltip));

    public static void SetIsEnabled(Control o, bool value) => o.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(Control o) => o.GetValue(IsEnabledProperty);

    /// <summary>The popup (whose child is a Border → TextBlock) this behavior positions and fills.</summary>
    public static readonly AttachedProperty<Popup?> CardProperty =
        AvaloniaProperty.RegisterAttached<Control, Popup?>("Card", typeof(HeatmapTooltip));

    public static void SetCard(Control o, Popup? value) => o.SetValue(CardProperty, value);
    public static Popup? GetCard(Control o) => o.GetValue(CardProperty);

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
        if (GetCard(owner) is not { } popup) return;

        // Refresh the text only when over an actual day cell; in the gaps between cells
        // keep the last text so it doesn't flicker as the pointer crosses.
        if (FindCell(e.Source as Visual, owner) is { } cell
            && popup.Child is Border { Child: TextBlock text })
            text.Text = cell.Tooltip;

        // Place the card centered above the cursor. PlacementRect is in the target's
        // coordinate space; lifting the anchor up by Gap keeps the popup clear of the
        // pointer. Setting these while open repositions the existing popup (no flicker).
        var pos = e.GetPosition(owner);
        popup.PlacementTarget = owner;
        popup.PlacementRect = new Rect(pos.X, pos.Y - Gap, 1, 1);
        if (!popup.IsOpen) popup.IsOpen = true;
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (GetCard((Control)sender!) is { } popup) popup.IsOpen = false;
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
