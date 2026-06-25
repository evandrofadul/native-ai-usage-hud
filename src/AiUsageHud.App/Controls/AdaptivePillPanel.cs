using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AiUsageHud.App.Controls;

/// <summary>
/// Lays out tab "pills" as an equal-width strip that fills the whole available width:
/// every shown pill gets the same width, so the row always spans edge to edge. Each pill
/// is kept at least <see cref="MinChildWidth"/> wide — and never narrower than the widest
/// pill's natural size, so icons/labels never clip. When the strip is too narrow to honor
/// that minimum for all pills, some are dropped (collapsed to zero) until the rest fit,
/// like a segmented control that sheds options instead of squishing them.
///
/// The dropped pills are normally the trailing (rightmost) ones, but the visible run is a
/// contiguous window that always keeps the <em>selected</em> pill on screen: if the
/// selection would fall past the trailing cut, leading pills are dropped instead so the
/// selected pill stays as the rightmost visible one. That stops the sliding indicator from
/// vanishing (and the selection from appearing to clear) when the window shrinks while a
/// trailing vendor is active.
///
/// Drop-in <c>ItemsPanel</c> for the vendor TabControl (see Themes/Controls/Tabs.axaml).
/// Hidden pills are arranged to an empty rect (not toggled via IsVisible) so a single layout
/// pass settles without re-invalidating measure.
/// </summary>
public sealed class AdaptivePillPanel : Panel
{
    /// <summary>Smallest width a single pill may take before pills are dropped.</summary>
    public static readonly StyledProperty<double> MinChildWidthProperty =
        AvaloniaProperty.Register<AdaptivePillPanel, double>(nameof(MinChildWidth), 80d);

    public double MinChildWidth
    {
        get => GetValue(MinChildWidthProperty);
        set => SetValue(MinChildWidthProperty, value);
    }

    /// <summary>Largest natural pill width, carried from measure to arrange so the visible
    /// count is computed identically in both passes.</summary>
    private double _minSlot = 1d;

    /// <summary>The owning TabControl — watched so a selection change re-arranges the window
    /// (cycling to a currently-dropped pill must bring it back into view).</summary>
    private TabControl? _owner;

    static AdaptivePillPanel()
    {
        AffectsMeasure<AdaptivePillPanel>(MinChildWidthProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _owner = this.FindAncestorOfType<TabControl>();
        if (_owner is not null) _owner.SelectionChanged += OnOwnerSelectionChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_owner is not null) _owner.SelectionChanged -= OnOwnerSelectionChanged;
        _owner = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnOwnerSelectionChanged(object? sender, SelectionChangedEventArgs e) => InvalidateArrange();

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children;
        int count = children.Count;
        if (count == 0) return default;

        // Per-pill floor: the configured minimum, raised to the widest pill's natural width
        // so nothing clips even at the tightest fitting size.
        double min = MinChildWidth;
        for (int i = 0; i < count; i++)
        {
            children[i].Measure(Size.Infinity);
            min = Math.Max(min, children[i].DesiredSize.Width);
        }
        _minSlot = min = Math.Max(min, 1d);

        int visible = VisibleCount(availableSize.Width, min, count, out double slot);

        double height = 0;
        for (int i = 0; i < visible; i++)
        {
            var child = children[i];
            child.Measure(new Size(slot, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double width = double.IsInfinity(availableSize.Width) ? slot * visible : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children;
        int count = children.Count;
        if (count == 0) return finalSize;

        int visible = VisibleCount(finalSize.Width, _minSlot, count, out double slot);
        int start = WindowStart(children, visible, count);

        double x = 0;
        for (int i = 0; i < count; i++)
        {
            if (i >= start && i < start + visible)
            {
                children[i].Arrange(new Rect(x, 0, slot, finalSize.Height));
                x += slot;
            }
            else
            {
                children[i].Arrange(default);  // dropped — give it no area so it can't render or be hit
            }
        }
        return finalSize;
    }

    /// <summary>How many pills fit at <paramref name="min"/> width, and the equal width each
    /// shown pill then gets so the strip fills <paramref name="available"/>.</summary>
    private static int VisibleCount(double available, double min, int count, out double slot)
    {
        if (double.IsInfinity(available))
        {
            slot = min;
            return count;
        }

        int visible = Math.Clamp((int)(available / min), 1, count);
        slot = available / visible;
        return visible;
    }

    /// <summary>First index of the contiguous visible run: 0 (drop trailing) unless the
    /// selected pill sits past the trailing cut, in which case it slides right (dropping
    /// leading pills) just far enough to keep the selection visible.</summary>
    private static int WindowStart(global::Avalonia.Controls.Controls children, int visible, int count)
    {
        if (visible >= count) return 0;

        int selected = SelectedIndex(children);
        if (selected < visible) return 0;   // -1 (none) or already within the leading window

        return Math.Clamp(selected - visible + 1, 0, count - visible);
    }

    private static int SelectedIndex(global::Avalonia.Controls.Controls children)
    {
        for (int i = 0; i < children.Count; i++)
            if (children[i] is TabItem { IsSelected: true }) return i;
        return -1;
    }
}
