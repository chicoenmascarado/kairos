using System.Runtime.InteropServices;

namespace KairosDock.Services;

/// <summary>
/// Reads and controls the system bits the Control Center exposes: battery (read),
/// master volume (read/write via Core Audio), and display brightness (read/write
/// via WMI on laptops). Everything is best-effort and guarded so a machine without
/// a battery or with no WMI brightness simply reports "unavailable".
/// </summary>
internal static class SystemStatus
{
    // ===================== Battery =====================

    public readonly record struct BatteryInfo(bool Present, int Percent, bool Charging);

    public static BatteryInfo GetBattery()
    {
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS s))
            return new BatteryInfo(false, 0, false);

        bool noBattery = (s.BatteryFlag & 128) != 0; // bit 7 = no system battery
        int pct = s.BatteryLifePercent == 255 ? 0 : s.BatteryLifePercent;
        bool charging = s.ACLineStatus == 1;
        return new BatteryInfo(!noBattery, pct, charging);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    // ===================== Volume (Core Audio) =====================

    /// <summary>Master volume 0..1, or -1 if unavailable.</summary>
    public static float GetVolume()
    {
        try
        {
            var ep = GetEndpointVolume();
            if (ep == null) return -1;
            ep.GetMasterVolumeLevelScalar(out float v);
            Marshal.ReleaseComObject(ep);
            return v;
        }
        catch { return -1; }
    }

    public static void SetVolume(float scalar)
    {
        try
        {
            var ep = GetEndpointVolume();
            if (ep == null) return;
            Guid ctx = Guid.Empty;
            ep.SetMasterVolumeLevelScalar(Math.Clamp(scalar, 0f, 1f), ref ctx);
            Marshal.ReleaseComObject(ep);
        }
        catch { /* best-effort */ }
    }

    private static IAudioEndpointVolume? GetEndpointVolume()
    {
        var enumType = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
        if (enumType == null) return null;
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumType)!;
        enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out IMMDevice device);
        Guid iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out object o);
        Marshal.ReleaseComObject(device);
        Marshal.ReleaseComObject(enumerator);
        return o as IAudioEndpointVolume;
    }

    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        // (rest of the vtable unused)
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        // (rest unused)
    }

    // Full vtable up to the methods we call (order is critical for COM).
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDB, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float levelDB);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDB, ref Guid ctx);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        int GetChannelVolumeLevel(uint channel, out float levelDB);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // ===================== Dark mode (registry) =====================

    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True if Windows is in dark mode (apps).</summary>
    public static bool IsDarkMode()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
            return v is int i && i == 0;
        }
        catch { return false; }
    }

    /// <summary>Switches Windows between dark and light (apps + system).</summary>
    public static void SetDarkMode(bool dark)
    {
        try
        {
            int light = dark ? 0 : 1;
            Microsoft.Win32.Registry.SetValue(PersonalizeKey, "AppsUseLightTheme", light, Microsoft.Win32.RegistryValueKind.DWord);
            Microsoft.Win32.Registry.SetValue(PersonalizeKey, "SystemUsesLightTheme", light, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[KairosDock] dark mode failed: {ex.Message}"); }
    }

    // ===================== Media keys =====================
    private const byte VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static void MediaPlayPause() => TapKey(VK_MEDIA_PLAY_PAUSE);
    public static void MediaNext() => TapKey(VK_MEDIA_NEXT);
    public static void MediaPrevious() => TapKey(VK_MEDIA_PREV);

    private static void TapKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // ===================== Brightness (WMI, laptops) =====================

    public static bool BrightnessAvailable { get; private set; }

    /// <summary>Current brightness 0..100, or -1 if unavailable.</summary>
    public static int GetBrightness()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var o in searcher.Get())
            {
                BrightnessAvailable = true;
                return Convert.ToInt32(o["CurrentBrightness"]);
            }
        }
        catch { /* not a laptop / no WMI provider */ }
        BrightnessAvailable = false;
        return -1;
    }

    public static void SetBrightness(int percent)
    {
        try
        {
            percent = Math.Clamp(percent, 0, 100);
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (System.Management.ManagementObject o in searcher.Get())
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
        }
        catch { /* best-effort */ }
    }
}
