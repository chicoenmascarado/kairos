using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using KairosDock.Interop;

namespace KairosDock.Services;

/// <summary>
/// Enumerates the Windows notification area (system tray) — including the hidden
/// "overflow" icons — by reading explorer.exe's tray <c>ToolbarWindow32</c> across
/// processes (VirtualAllocEx + ReadProcessMemory). For each tray button we recover
/// the owning window, its icon and tooltip.
///
/// This is the classic Win10 / LTSC notification area. (Windows 11's newer XAML tray
/// host doesn't expose a ToolbarWindow32, so there this returns empty — handled
/// gracefully by the caller.)
/// </summary>
internal static class SystemTray
{
    /// <param name="Version">NOTIFYICON protocol the app registered (0 classic, 3/4
    /// modern), or -1 when unknown (read from explorer's toolbar).</param>
    public readonly record struct TrayItem(
        BitmapSource? Icon, string Tooltip, IntPtr OwnerWindow, uint Id, uint CallbackMessage,
        int Version = -1);

    /// <summary>
    /// Replays a tray click to the owning app, exactly like the real notification
    /// area: the app then activates (left) or pops its own context menu with the
    /// same Open/Close/Exit options (right). Uses the classic NOTIFYICON protocol
    /// (wParam = icon id, lParam = mouse message).
    /// </summary>
    public static void SendClick(TrayItem item, bool rightClick)
    {
        if (item.OwnerWindow == IntPtr.Zero || item.CallbackMessage == 0)
            return;

        // The owner must be foreground for its popup menu to appear and dismiss.
        GetWindowThreadProcessId(item.OwnerWindow, out uint ownerPid);
        AllowSetForegroundWindow(ownerPid);
        SetForegroundWindow(item.OwnerWindow);
        GetCursorPos(out POINT pt);

        IntPtr hwnd = item.OwnerWindow;
        uint cb = item.CallbackMessage;
        uint id = item.Id;
        uint down = rightClick ? WM_RBUTTONDOWN : WM_LBUTTONDOWN;
        uint up = rightClick ? WM_RBUTTONUP : WM_LBUTTONUP;
        IntPtr coords = (IntPtr)MakeLong(pt.X, pt.Y);

        // Known protocol (icons from TrayHost): send exactly what explorer would.
        if (item.Version == 0)
        {
            PostMessage(hwnd, cb, (IntPtr)id, (IntPtr)down);
            PostMessage(hwnd, cb, (IntPtr)id, (IntPtr)up);
            return;
        }
        if (item.Version > 0)
        {
            bool v4 = item.Version >= 4;
            IntPtr w = v4 ? coords : (IntPtr)id;
            IntPtr L(uint m) => v4 ? (IntPtr)MakeLong((int)m, (int)id) : (IntPtr)m;
            PostMessage(hwnd, cb, w, L(down));
            PostMessage(hwnd, cb, w, L(up));
            PostMessage(hwnd, cb, w, L(rightClick ? (uint)WM_CONTEXTMENU : NIN_SELECT));
            return;
        }

        // Unknown protocol: send both, the app ignores the one it doesn't speak.
        // 1) Classic protocol (Shell_NotifyIcon < v4): wParam = icon id, lParam = msg.
        PostMessage(hwnd, cb, (IntPtr)id, (IntPtr)down);
        PostMessage(hwnd, cb, (IntPtr)id, (IntPtr)up);

        // 2) NOTIFYICON_VERSION_4 protocol: wParam = cursor (x,y), lParam packs the
        //    notification event in the low word and the icon id in the high word.
        //    Apps respond to whichever protocol they registered with; the other is
        //    harmlessly ignored, so left- and right-click work across virtually all.
        PostMessage(hwnd, cb, coords, (IntPtr)MakeLong((int)down, (int)id));
        PostMessage(hwnd, cb, coords, (IntPtr)MakeLong((int)up, (int)id));
        if (rightClick)
            PostMessage(hwnd, cb, coords, (IntPtr)MakeLong(WM_CONTEXTMENU, (int)id));
    }

    private static int MakeLong(int low, int high) => (low & 0xffff) | (high << 16);

    public static List<TrayItem> Enumerate()
    {
        // Kairos' own tray (Win10 + Win11) when it's running; explorer's toolbar otherwise.
        if (TrayHost.IsActive)
            return TrayHost.Snapshot();

        var items = new List<TrayItem>();
        foreach (IntPtr toolbar in FindTrayToolbars())
            ReadToolbar(toolbar, items);
        return items;
    }

    private static IEnumerable<IntPtr> FindTrayToolbars()
    {
        // Visible icons: Shell_TrayWnd → TrayNotifyWnd → SysPager → ToolbarWindow32
        IntPtr tray = TrayHost.FindExplorerTray();
        IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        IntPtr pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
        IntPtr main = FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null);
        if (main != IntPtr.Zero) yield return main;

        // Hidden / overflow icons: NotifyIconOverflowWindow → ToolbarWindow32
        IntPtr overflowHost = FindWindow("NotifyIconOverflowWindow", null);
        IntPtr overflow = FindWindowEx(overflowHost, IntPtr.Zero, "ToolbarWindow32", null);
        if (overflow != IntPtr.Zero) yield return overflow;
    }

    private static void ReadToolbar(IntPtr toolbar, List<TrayItem> items)
    {
        int count = (int)SendMessage(toolbar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0)
            return;

        GetWindowThreadProcessId(toolbar, out uint pid);
        IntPtr proc = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
        if (proc == IntPtr.Zero)
            return;

        // One scratch buffer in explorer's address space, reused for every query.
        IntPtr remote = VirtualAllocEx(proc, IntPtr.Zero, 4096, MEM_COMMIT, PAGE_READWRITE);
        if (remote == IntPtr.Zero) { CloseHandle(proc); return; }

        try
        {
            for (int i = 0; i < count; i++)
            {
                // TB_GETBUTTON writes a TBBUTTON into explorer's buffer; read it back.
                SendMessage(toolbar, TB_GETBUTTON, new IntPtr(i), remote);
                var btn = ReadStruct<TBBUTTON>(proc, remote);

                if ((btn.fsState & TBSTATE_HIDDEN) != 0)
                    continue;
                if (btn.dwData == IntPtr.Zero)
                    continue;

                // The button's dwData points at explorer's per-icon record. Its first
                // field is the owning window handle; an icon handle lives a little
                // further in (offset varies by OS, so we probe for a valid one).
                byte[] tray = ReadBytes(proc, btn.dwData, 64);
                // TRAYDATA layout: hWnd(0), uID(8), uCallbackMessage(12), …
                IntPtr owner = (IntPtr)BitConverter.ToInt64(tray, 0);
                uint uid = BitConverter.ToUInt32(tray, 8);
                uint callback = BitConverter.ToUInt32(tray, 12);

                BitmapSource? icon = FindIcon(tray) ?? IconFromWindow(owner);
                string tip = GetButtonText(toolbar, proc, remote, btn.idCommand);
                if (string.IsNullOrWhiteSpace(tip))
                    tip = TitleFromWindow(owner);

                if (icon != null || !string.IsNullOrWhiteSpace(tip))
                    items.Add(new TrayItem(icon, tip ?? "", owner, uid, callback));
            }
        }
        finally
        {
            VirtualFreeEx(proc, remote, 0, MEM_RELEASE);
            CloseHandle(proc);
        }
    }

    /// <summary>Probe the tray record for a value that is actually a valid HICON.</summary>
    private static BitmapSource? FindIcon(byte[] trayRecord)
    {
        for (int off = 8; off + 8 <= trayRecord.Length; off += 8)
        {
            IntPtr cand = (IntPtr)BitConverter.ToInt64(trayRecord, off);
            if (cand == IntPtr.Zero)
                continue;
            if (!GetIconInfo(cand, out ICONINFO ii))
                continue;
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(cand, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
            catch { /* not really an icon — keep probing */ }
        }
        return null;
    }

    private static BitmapSource? IconFromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        string? path = ProcessPath(pid);
        return path == null ? null : IconExtractor.FromPath(path);
    }

    private static string GetButtonText(IntPtr toolbar, IntPtr proc, IntPtr remote, int cmd)
    {
        int len = (int)SendMessage(toolbar, TB_GETBUTTONTEXTW, new IntPtr(cmd), remote);
        if (len <= 0)
            return "";
        byte[] raw = ReadBytes(proc, remote, (len + 1) * 2);
        return Encoding.Unicode.GetString(raw, 0, len * 2);
    }

    private static string TitleFromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        GetWindowThreadProcessId(hwnd, out uint pid);
        string? path = ProcessPath(pid);
        return path == null ? "" : System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private static string? ProcessPath(uint pid)
    {
        if (pid == 0) return null;
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int cap = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref cap) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    private static T ReadStruct<T>(IntPtr proc, IntPtr addr) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buf = ReadBytes(proc, addr, size);
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }

    private static byte[] ReadBytes(IntPtr proc, IntPtr addr, int size)
    {
        byte[] buf = new byte[size];
        ReadProcessMemory(proc, addr, buf, size, out _);
        return buf;
    }

    // --- structs / constants ----------------------------------------------
    private const uint TB_BUTTONCOUNT = 0x0418;
    private const uint TB_GETBUTTON = 0x0417;
    private const uint TB_GETBUTTONTEXTW = 0x044B;
    private const byte TBSTATE_HIDDEN = 0x08;

    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const uint NIN_SELECT = 0x0400; // WM_USER: "icon selected" for v3+ apps

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TBBUTTON
    {
        public int iBitmap;
        public int idCommand;
        public byte fsState;
        public byte fsStyle;
        public byte r0, r1, r2, r3, r4, r5; // 6 bytes padding (x64 aligns dwData to 8)
        public IntPtr dwData;
        public IntPtr iString;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, int size, uint type, uint protect);
    [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, int size, uint type);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr proc, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);
}
