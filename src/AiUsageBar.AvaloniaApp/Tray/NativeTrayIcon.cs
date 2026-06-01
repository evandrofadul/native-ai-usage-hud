using System.Runtime.InteropServices;

namespace AiUsageBar.AvaloniaApp.Tray;

/// <summary>
/// Minimal Win32 <c>Shell_NotifyIcon</c> wrapper. A message-only window (created on the
/// UI thread, so Avalonia's message pump dispatches to it) receives the tray callbacks
/// and re-raises them as events. This is what lets us drive a fully Avalonia-styled
/// context menu and hover card, which Avalonia's built-in <c>TrayIcon</c> can't do.
/// </summary>
internal sealed class NativeTrayIcon : IDisposable
{
    /// <summary>Left click (or NIN_SELECT) on the icon.</summary>
    public event Action? LeftClick;

    /// <summary>Right click / context-menu request, with the screen coordinates.</summary>
    public event Action<int, int>? RightClick;

    /// <summary>Pointer moved over the icon.</summary>
    public event Action? MouseMove;

    public NativeTrayIcon()
    {
        _wndProc = WindowProc; // keep the delegate alive for the lifetime of the window
        _className = "AiUsageBarTray_" + Guid.NewGuid().ToString("N");

        var hInstance = GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = _className,
        };
        RegisterClassEx(ref wc);
        _hwnd = CreateWindowEx(0, _className, string.Empty, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    /// <summary>Add (or update) the tray icon. Returns false if the shell rejected it.</summary>
    public bool Show(IntPtr hIcon, string tip)
    {
        _hIcon = hIcon;
        var data = NewData(NifMessage | NifIcon | NifTip);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = hIcon;
        data.szTip = Trim(tip, 127);

        var ok = Shell_NotifyIcon(_added ? NimModify : NimAdd, ref data);
        if (ok && !_added)
        {
            _added = true;
            var v = NewData(0);
            v.uVersion = NotifyiconVersion4;
            Shell_NotifyIcon(NimSetVersion, ref v);
        }
        return ok;
    }

    /// <summary>Swap the glyph, freeing the previously shown icon handle.</summary>
    public void SetIcon(IntPtr hIcon)
    {
        if (!_added) return;
        var old = _hIcon;
        _hIcon = hIcon;
        var data = NewData(NifIcon);
        data.hIcon = hIcon;
        Shell_NotifyIcon(NimModify, ref data);
        if (old != IntPtr.Zero && old != hIcon) DestroyIcon(old);
    }

    public void SetTip(string tip)
    {
        if (!_added) return;
        var data = NewData(NifTip);
        data.szTip = Trim(tip, 127);
        Shell_NotifyIcon(NimModify, ref data);
    }

    /// <summary>The icon's screen rectangle (physical pixels), for positioning popups.</summary>
    public bool TryGetRect(out RECT rect)
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = IconId,
        };
        return Shell_NotifyIconGetRect(ref id, out rect) == 0; // S_OK
    }

    public static bool TryGetCursorPos(out int x, out int y)
    {
        var ok = GetCursorPos(out var p);
        x = p.X; y = p.Y;
        return ok;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: LOWORD(lParam) = event; wParam packs the screen X/Y.
            var evt = (uint)((long)lParam & 0xFFFF);
            var x = (short)((long)wParam & 0xFFFF);
            var y = (short)(((long)wParam >> 16) & 0xFFFF);
            switch (evt)
            {
                case WmLButtonUp:
                case NinSelect:
                    LeftClick?.Invoke();
                    break;
                case WmRButtonUp:
                case WmContextMenu:
                    RightClick?.Invoke(x, y);
                    break;
                case WmMouseMove:
                    MouseMove?.Invoke();
                    break;
            }
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        UnregisterClass(_className, GetModuleHandle(null));
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
    }

    private NOTIFYICONDATA NewData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    // ---- state ----
    private readonly WndProc _wndProc;
    private readonly string _className;
    private readonly IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _added;

    // ---- constants ----
    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const uint NifMessage = 1, NifIcon = 2, NifTip = 4;
    private const uint NotifyiconVersion4 = 4;
    private const uint WmLButtonUp = 0x0202, WmRButtonUp = 0x0205, WmMouseMove = 0x0200, WmContextMenu = 0x007B;
    private const uint NinSelect = 0x0400; // WM_USER
    private static readonly IntPtr HwndMessage = new(-3);

    // ---- interop ----
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
