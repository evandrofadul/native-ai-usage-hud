using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace AiUsageHud.App.Behaviors;

/// <summary>
/// Turns a single indicator <see cref="Border"/> into a sliding "pill" that follows the
/// selected tab — the segmented-control effect where one highlight glides (and resizes)
/// from the old tab to the new one, instead of each tab painting its own background.
///
/// The indicator lives <em>behind</em> the headers in the TabControl template; the tabs
/// themselves are transparent and only recolor their label on selection. On every
/// selection the pill animates its <see cref="Layoutable.Width"/> and a <c>translateX</c>
/// to match the selected header's bounds; the first placement snaps (no fly-in from the
/// corner). Because there is one moving element, switching never crossfades, so it can't
/// flicker.
///
/// Wire it on the indicator border inside the TabControl template:
///   <code>behaviors:SlidingPill.IsEnabled="True"</code>
/// The border must be a child of a panel it shares with the <c>PART_ItemsPresenter</c>.
/// </summary>
public static class SlidingPill
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(240);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("IsEnabled", typeof(SlidingPill));

    public static void SetIsEnabled(Border o, bool value) => o.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(Border o) => o.GetValue(IsEnabledProperty);

    /// <summary>Per-indicator wiring, kept off the control so the class stays static.</summary>
    private sealed class State
    {
        public TabControl? Tabs;
        public Transitions? Transitions;
        public EventHandler<SelectionChangedEventArgs>? OnSelection;
        public EventHandler? OnLayout;
        // Geometry of the selected header the pill is currently placed against. NaN until
        // the first placement; used to skip redundant layout-driven repositioning.
        public double TargetX = double.NaN;
        public double TargetWidth = double.NaN;
    }

    private static readonly ConditionalWeakTable<Border, State> States = new();

    static SlidingPill()
    {
        IsEnabledProperty.Changed.AddClassHandler<Border>((pill, e) =>
        {
            if (e.GetNewValue<bool>())
            {
                pill.AttachedToVisualTree += OnAttached;
                pill.DetachedFromVisualTree += OnDetached;
            }
            else
            {
                pill.AttachedToVisualTree -= OnAttached;
                pill.DetachedFromVisualTree -= OnDetached;
                Teardown(pill);
            }
        });
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var pill = (Border)sender!;
        if (pill.FindAncestorOfType<TabControl>() is not { } tabs) return;

        var st = States.GetValue(pill, _ => new State());
        st.Tabs = tabs;
        st.TargetX = double.NaN;
        st.TargetWidth = double.NaN;
        st.Transitions =
        [
            new DoubleTransition { Property = Layoutable.WidthProperty, Duration = Duration, Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Duration, Easing = new CubicEaseOut() },
        ];

        // Slide on selection; snap on every layout pass so the pill tracks the first
        // placement and any later resize (the headers are equal-width and grow/shrink with
        // the window). Place() self-skips when the selected header hasn't actually moved,
        // so the selection width tween — which itself triggers layout — isn't cancelled.
        st.OnSelection = (_, _) => Place(pill, animated: true);
        st.OnLayout = (_, _) => Place(pill, animated: false);
        tabs.SelectionChanged += st.OnSelection;
        tabs.LayoutUpdated += st.OnLayout;
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Teardown((Border)sender!);

    private static void Teardown(Border pill)
    {
        if (!States.TryGetValue(pill, out var st)) return;
        if (st.Tabs is { } tabs)
        {
            if (st.OnSelection is not null) tabs.SelectionChanged -= st.OnSelection;
            if (st.OnLayout is not null) tabs.LayoutUpdated -= st.OnLayout;
        }
        States.Remove(pill);
    }

    private static void Place(Border pill, bool animated)
    {
        if (!States.TryGetValue(pill, out var st) || st.Tabs is not { } tabs) return;
        if (tabs.SelectedIndex is var idx && idx < 0) return;
        if (tabs.ContainerFromIndex(idx) is not Control header) return;
        if (pill.GetVisualParent() is not { } panel) return;
        if (header.TranslatePoint(default, panel) is not { } topLeft) return;
        var width = header.Bounds.Width;
        if (width <= 1) return;   // not measured yet — wait for the next layout pass
        var x = topLeft.X;

        // Layout-driven (snap) calls are no-ops unless the selected header truly moved or
        // resized — so the per-frame layout from the selection width tween, or any unrelated
        // layout pass, neither cancels the animation nor churns transform allocations.
        if (!animated && Close(x, st.TargetX) && Close(width, st.TargetWidth)) return;

        // Snap with transitions off (first placement / resize); selection animates. Re-arm
        // after a snap so the next selection still glides instead of jumping.
        pill.Transitions = animated ? st.Transitions : null;

        pill.Width = width;
        pill.RenderTransform = TransformOperations.Parse(
            string.Format(CultureInfo.InvariantCulture, "translateX({0}px)", x));
        st.TargetX = x;
        st.TargetWidth = width;

        if (!animated)
            pill.Transitions = st.Transitions;
    }

    private static bool Close(double a, double b) => Math.Abs(a - b) < 0.5;
}
