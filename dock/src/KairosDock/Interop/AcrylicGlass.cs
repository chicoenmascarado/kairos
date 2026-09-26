using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KairosDock.Interop;

/// <summary>
/// Enables a real DWM blur-behind on a WPF window via the (undocumented but
/// widely used) <c>SetWindowCompositionAttribute</c> accent API, so the dock's
/// translucent panel actually blurs the desktop behind it instead of just being
/// see-through.
///
/// We deliberately use ACCENT_ENABLE_BLURBEHIND rather than the acrylic variant:
/// blur-behind composes cleanly with WPF per-pixel transparency (AllowsTransparency
/// = true), which is what lets us keep our 24px rounded corners and soft drop
/// shadow. The whole thing is best-effort and wrapped by the caller in try/catch —
/// if the OS refuses, the layered semi-transparent glass still looks great on its
/// own.
/// </summary>
internal static class AcrylicGlass
{
    public static void TryEnable(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND,
            // GradientColor is 0xAABBGGRR. A faint tint here deepens the glass; we
            // keep it light because our WPF panel already carries the violet tint.
            GradientColor = 0x10_0F_0D_0D,
            AccentFlags = 0,
            AnimationId = 0
        };

        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = size,
                Data = ptr
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WindowCompositionAttributeData data);
}
