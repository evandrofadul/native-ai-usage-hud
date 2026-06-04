using Avalonia;
using Avalonia.Controls;

namespace AiUsageBar.AvaloniaApp.Behaviors;

/// <summary>
/// Keeps a horizontally-scrollable <see cref="ScrollViewer"/> pinned to its right
/// edge. Used by the activity heatmap so that when the card is too narrow to show
/// every column, the <em>oldest</em> (left) days scroll out of view first and the
/// most recent days stay visible — instead of the newest being clipped on the right.
///
/// Wire it on the ScrollViewer:
///   <code>behaviors:ScrollPinEnd.IsEnabled="True"</code>
/// The viewer should have <c>HorizontalScrollBarVisibility="Hidden"</c> (scrollable,
/// but no visible bar) and <c>VerticalScrollBarVisibility="Disabled"</c>.
/// </summary>
public static class ScrollPinEnd
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(ScrollPinEnd));

    public static void SetIsEnabled(ScrollViewer o, bool value) => o.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(ScrollViewer o) => o.GetValue(IsEnabledProperty);

    static ScrollPinEnd()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
        {
            if (e.GetNewValue<bool>())
            {
                sv.PropertyChanged += OnPropertyChanged;
                Pin(sv);
            }
            else
            {
                sv.PropertyChanged -= OnPropertyChanged;
            }
        });
    }

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Re-pin whenever the content size (Extent) or the visible area (Viewport)
        // changes — i.e. on refresh and on window resize.
        if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty)
            Pin((ScrollViewer)sender!);
    }

    private static void Pin(ScrollViewer sv)
    {
        var max = System.Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        if (System.Math.Abs(sv.Offset.X - max) > 0.5)
            sv.Offset = new Vector(max, sv.Offset.Y);
    }
}
