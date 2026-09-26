using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KairosDock.Services;

/// <summary>
/// Kairos' own notification area. Works the same on Windows 10 and Windows 11,
/// where explorer's XAML tray can no longer be read from outside.
///
/// Apps publish tray icons with Shell_NotifyIcon, which sends a WM_COPYDATA to the
/// first top-level window of class "Shell_TrayWnd". We register a hidden window of
/// that class and keep it above explorer's in z-order, so we receive every icon
/// add / change / delete. Every message is then forwarded untouched to explorer's
/// real taskbar, so explorer keeps working (balloons, overflow, appbars) and
/// nothing breaks if the dock exits: our window dies with the process and
/// Shell_NotifyIcon simply finds explorer's again.
///
/// Same technique as RetroBar / Cairo's ManagedShell.
/// </summary>
internal static class TrayHost
{
    public sealed class Icon
    {
        public IntPtr OwnerWindow;
        public uint Id;
        public Guid Guid;
        public uint CallbackMessage;
        public uint Version;
        public bool Hidden;
        public string Tooltip = "";
        public BitmapSource? Image;
    }

    private static readonly List<Icon> _icons = new();
    private static IntPtr _hwnd;
    private static IntPtr _explorerTray;
    private static WndProcDelegate? _wndProc; // kept alive: the OS holds a pointer to it
    private static uint _taskbarCreatedMsg;
    private static System.Windows.Threading.DispatcherTimer? _keeper;

    /// <summary>True once our tray window is up and receiving icons.</summary>
    public static bool IsActive => _hwnd != IntPtr.Zero;

    /// <summary>Raised on the UI thread when explorer's taskbar was (re)created.</summary>
    public static event Action? ExplorerTaskbarRecreated;

    /// <summary>Creates the tray window. Call once, on the UI thread.</summary>
    public static void Start()
    {
        if (_hwnd != IntPtr.Zero)
            return;

        // Taskbar skinners (TranslucentTB) inject into every "Shell_TrayWnd" and
        // crash any process that isn't explorer. With one running, leave the tray
        // to explorer rather than take the dock down.
        if (Process.GetProcessesByName("TranslucentTB").Length > 0)
            return;

        try
        {
            _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
            _explorerTray = FindExplorerTray();

            _wndProc = WndProc;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = "Shell_TrayWnd",
            };
            if (RegisterClassEx(ref wc) == 0)
                return;

            _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, "Shell_TrayWnd", "",
                WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                return;

            PutOnTop();

            // If the dock ever runs elevated, normal apps' icon messages would be
            // blocked by UIPI: let WM_COPYDATA through explicitly.
            ChangeWindowMessageFilterEx(_hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);

            // Ask every running app to re-register its icons: they now land here.
            PostMessage(HWND_BROADCAST, _taskbarCreatedMsg, IntPtr.Zero, IntPtr.Zero);

            // Explorer can reorder itself above us (or restart): check every 2 s.
            _keeper = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _keeper.Tick += (_, _) => Keep();
            _keeper.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] tray host failed: {ex.Message}");
            _hwnd = IntPtr.Zero;
        }
    }

    public static void Stop()
    {
        _keeper?.Stop();
        if (_hwnd != IntPtr.Zero)
            DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    /// <summary>Visible icons whose owning app is still alive.</summary>
    public static List<SystemTray.TrayItem> Snapshot()
    {
        _icons.RemoveAll(i => !IsWindow(i.OwnerWindow));
        return _icons
            .Where(i => !i.Hidden && i.Image != null)
            .Select(i => new SystemTray.TrayItem(i.Image, i.Tooltip, i.OwnerWindow, i.Id,
                                                 i.CallbackMessage, (int)i.Version))
            .ToList();
    }

    /// <summary>
    /// Explorer's real taskbar: the "Shell_TrayWnd" that isn't ours. Use this instead
    /// of FindWindow("Shell_TrayWnd"), which now returns the Kairos one.
    /// </summary>
    public static IntPtr FindExplorerTray()
    {
        IntPtr w = IntPtr.Zero;
        while ((w = FindWindowEx(IntPtr.Zero, w, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            if (w != _hwnd)
                return w;
        }
        return IntPtr.Zero;
    }

    private static void Keep()
    {
        IntPtr explorer = FindExplorerTray();
        bool recreated = explorer != IntPtr.Zero && explorer != _explorerTray;
        _explorerTray = explorer;

        if (FindWindow("Shell_TrayWnd", null) != _hwnd)
            PutOnTop();

        if (recreated)
        {
            // Explorer restarted: apps re-add their icons on its broadcast, but some
            // may have reached the new taskbar before we were back on top.
            PostMessage(HWND_BROADCAST, _taskbarCreatedMsg, IntPtr.Zero, IntPtr.Zero);
            ExplorerTaskbarRecreated?.Invoke();
        }
    }

    private static void PutOnTop() =>
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // -----------------------------------------------------------------------

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_COPYDATA && lParam != IntPtr.Zero)
            {
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                bool handled = false;
                if (cds.dwData == (IntPtr)1 && cds.lpData != IntPtr.Zero)
                    handled = OnNotifyIcon(cds.lpData, cds.cbData);

                IntPtr result = Forward(msg, wParam, lParam);
                // If explorer isn't there (restarting), still tell the app it worked.
                return result == IntPtr.Zero && handled ? (IntPtr)1 : result;
            }

            if (msg == _taskbarCreatedMsg)
                return IntPtr.Zero; // our own / explorer's broadcast: nothing to forward

            // Private taskbar messages some apps send straight to Shell_TrayWnd.
            if ((msg >= WM_USER && msg < 0xC000) || (msg == WM_SYSCOMMAND && (int)wParam == SC_TASKLIST))
                return Forward(msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KairosDock] tray message failed: {ex.Message}");
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static IntPtr Forward(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_explorerTray == IntPtr.Zero || !IsWindow(_explorerTray))
            _explorerTray = FindExplorerTray();
        if (_explorerTray == IntPtr.Zero)
            return IntPtr.Zero;
        SendMessageTimeout(_explorerTray, msg, wParam, lParam, SMTO_ABORTIFHUNG, 3000, out IntPtr result);
        return result;
    }

    // SHELLTRAYDATA = int signature, uint message, NOTIFYICONDATA (32-bit layout,
    // always, even from 64-bit apps). Offsets below are relative to the NID.
    private const int NidBase = 8;

    private static bool OnNotifyIcon(IntPtr data, int size)
    {
        if (size < NidBase + 24 || Marshal.ReadInt32(data) != 0x34753423)
            return false;

        uint message = (uint)Marshal.ReadInt32(data, 4);
        IntPtr nid = data + NidBase;
        int nidSize = size - NidBase;

        IntPtr owner = (IntPtr)(uint)Marshal.ReadInt32(nid, 4);
        uint id = (uint)Marshal.ReadInt32(nid, 8);
        uint flags = (uint)Marshal.ReadInt32(nid, 12);
        Guid guid = (flags & NIF_GUID) != 0 && nidSize >= 952 ? ReadGuid(nid + 936) : Guid.Empty;

        Icon? icon = _icons.FirstOrDefault(i =>
            guid != Guid.Empty ? i.Guid == guid : i.OwnerWindow == owner && i.Id == id);

        switch (message)
        {
            case NIM_ADD:
            case NIM_MODIFY:
                if (icon == null)
                {
                    icon = new Icon { OwnerWindow = owner, Id = id, Guid = guid };
                    _icons.Add(icon);
                }
                icon.OwnerWindow = owner;
                icon.Id = id;
                if ((flags & NIF_MESSAGE) != 0)
                    icon.CallbackMessage = (uint)Marshal.ReadInt32(nid, 16);
                if ((flags & NIF_ICON) != 0)
                    icon.Image = ToBitmap((IntPtr)(uint)Marshal.ReadInt32(nid, 20)) ?? icon.Image;
                if ((flags & NIF_TIP) != 0 && nidSize >= 24 + 256)
                    icon.Tooltip = Marshal.PtrToStringUni(nid + 24) ?? "";
                if ((flags & NIF_STATE) != 0 && nidSize >= 288)
                {
                    uint state = (uint)Marshal.ReadInt32(nid, 280);
                    uint mask = (uint)Marshal.ReadInt32(nid, 284);
                    if ((mask & NIS_HIDDEN) != 0)
                        icon.Hidden = (state & NIS_HIDDEN) != 0;
                }
                return true;

            case NIM_DELETE:
                if (icon != null)
                    _icons.Remove(icon);
                return true;

            case NIM_SETVERSION:
                if (icon != null && nidSize >= 804)
                    icon.Version = (uint)Marshal.ReadInt32(nid, 800);
                return true;

            default: // NIM_SETFOCUS
                return true;
        }
    }

    private static Guid ReadGuid(IntPtr p)
    {
        var b = new byte[16];
        Marshal.Copy(p, b, 0, 16);
        return new Guid(b);
    }

    private static BitmapSource? ToBitmap(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
            return null;
        // The app may destroy its icon later: take our own copy first.
        IntPtr copy = CopyIcon(hIcon);
        if (copy == IntPtr.Zero)
            return null;
        try
        {
            var bmp = Imaging.CreateBitmapSourceFromHIcon(copy, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(copy);
        }
    }

    // --- Win32 --------------------------------------------------------------

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    private const uint WM_COPYDATA = 0x004A, WM_USER = 0x0400, WM_SYSCOMMAND = 0x0112;
    private const int SC_TASKLIST = 0xF130;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_GUID = 0x20;
    private const uint NIS_HIDDEN = 0x1;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    private const uint SMTO_ABORTIFHUNG = 0x2;
    private const uint MSGFLT_ALLOW = 1;
    private static readonly IntPtr HWND_TOPMOST = new(-1), HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
