using System;
using System.Runtime.InteropServices;

namespace KairosKeys
{
    // Interop Win32 para: efecto acrylic (glass real tipo Apple) y hotkey global.
    internal static class Interop
    {
        // ---------- Acrylic / Blur ----------
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor;
            public uint AnimationId;
        }

        // ---------- Hotkey global ----------
        [DllImport("user32.dll")]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_SHIFT = 0x0004;
        internal const uint MOD_WIN = 0x0008;
        internal const uint MOD_NOREPEAT = 0x4000;
        internal const uint VK_SPACE = 0x20;

        // Teclas virtuales que usa KairosKeys
        internal const uint VK_E = 0x45;
        internal const uint VK_S = 0x53;
        internal const uint VK_D = 0x44;
        internal const uint VK_L = 0x4C;
        internal const uint VK_V = 0x56;
        internal const uint VK_M = 0x4D;
        internal const uint VK_PRIOR = 0x21; // Page Up
        internal const uint VK_NEXT = 0x22;  // Page Down
        internal const uint VK_UP = 0x26;
        internal const uint VK_DOWN = 0x28;

        // Teclas multimedia (para volumen) via keybd_event
        internal const byte VK_VOLUME_MUTE = 0xAD;
        internal const byte VK_VOLUME_DOWN = 0xAE;
        internal const byte VK_VOLUME_UP = 0xAF;
        internal const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        internal const int WM_HOTKEY = 0x0312;

        // ---------- Foreground ----------
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        // ---------- DWM (Win11 native backdrop + esquinas) ----------
        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // DWMWA_SYSTEMBACKDROP_TYPE = 38 ; valor 3 = Acrylic (Transient)
        internal const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        internal const int DWMSBT_TRANSIENTWINDOW = 3;

        // DWMWA_WINDOW_CORNER_PREFERENCE = 33 ; valor 2 = Round
        internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        internal const int DWMWCP_ROUND = 2;

        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
        internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // ---------- Extraccion de iconos ----------
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        internal const uint SHGFI_ICON = 0x000000100;
        internal const uint SHGFI_LARGEICON = 0x000000000;
        internal const uint SHGFI_SMALLICON = 0x000000001;
        internal const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    }
}
