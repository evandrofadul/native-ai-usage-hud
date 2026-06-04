using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AiUsageBar.AvaloniaApp.Tray;

/// <summary>
/// Renders the "ai" tray badge (a rounded square tinted by the active severity, with "ai"
/// in the window-background color) to an Avalonia <see cref="WindowIcon"/> using the Skia
/// render backend. This is the cross-platform counterpart to the Windows head's GDI+/HICON
/// path: it produces the icon the Linux <see cref="LinuxTrayController"/> hands to Avalonia's
/// <c>TrayIcon</c>, with no <c>System.Drawing</c> dependency.
/// </summary>
internal static class TrayGlyphRenderer
{
    // Rendered larger than the typical 22–24px tray slot so HiDPI hosts downscale cleanly.
    private const int Size = 64;

    /// <summary>Render the badge: <paramref name="bg"/> fills the rounded square, <paramref name="fg"/>
    /// draws the "ai" text. Must be called on the UI thread.</summary>
    public static WindowIcon Render(Color bg, Color fg)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            var rect = new Rect(0, 0, Size, Size);
            ctx.DrawRectangle(new SolidColorBrush(bg), null, new RoundedRect(rect, 12));

            var text = new FormattedText(
                "ai",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
                Size * 0.6,
                new SolidColorBrush(fg));

            var origin = new Point((Size - text.Width) / 2, (Size - text.Height) / 2);
            ctx.DrawText(text, origin);
        }

        return new WindowIcon(rtb);
    }
}
