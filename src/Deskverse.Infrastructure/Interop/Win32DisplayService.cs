namespace Deskverse.Infrastructure.Interop;

using System.Runtime.InteropServices;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;

/// <summary>
/// Enumerates physical displays through Win32 monitor APIs. Change detection is
/// explicit: call <see cref="CheckForChanges"/> to compare against the last
/// snapshot and raise <see cref="DisplaysChanged"/> when the topology differs.
/// </summary>
public sealed class Win32DisplayService : IDisplayService
{
    private IReadOnlyList<DisplayInfo> _lastSnapshot = [];

    public event EventHandler? DisplaysChanged;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr monitor, IntPtr hdcMonitor, ref RECT rect, IntPtr data) =>
            {
                var info = new MONITORINFOEXW();
                info.Size = (uint)Marshal.SizeOf<MONITORINFOEXW>();
                if (GetMonitorInfoW(monitor, ref info))
                {
                    var dpiScale = GetDpiScale(monitor);
                    displays.Add(new DisplayInfo(
                        info.DeviceName,
                        info.RcMonitor.Left,
                        info.RcMonitor.Top,
                        info.RcMonitor.Right - info.RcMonitor.Left,
                        info.RcMonitor.Bottom - info.RcMonitor.Top,
                        (info.DwFlags & MonitorInfoPrimary) != 0,
                        dpiScale));
                }

                return true;
            },
            IntPtr.Zero);

        _lastSnapshot = displays;
        return displays;
    }

    public DisplayInfo GetPrimaryDisplay()
    {
        var displays = GetDisplays();
        return displays.FirstOrDefault(d => d.IsPrimary)
            ?? new DisplayInfo("PRIMARY", 0, 0, 1920, 1080, true, 1.0);
    }

    /// <summary>Compares the current display topology with the last snapshot and raises the change event if it differs.</summary>
    public bool CheckForChanges()
    {
        var previous = _lastSnapshot;
        var current = GetDisplays();
        var changed = !current.Select(d => (d.DeviceName, d.Width, d.Height, d.X, d.Y))
            .SequenceEqual(previous.Select(d => (d.DeviceName, d.Width, d.Height, d.X, d.Y)));
        if (changed)
        {
            DisplaysChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    private static double GetDpiScale(IntPtr monitor)
    {
        try
        {
            var monitorDc = GetDC(monitor);
            if (monitorDc != IntPtr.Zero)
            {
                try
                {
                    var dpi = GetDeviceCaps(monitorDc, LogPixelsX);
                    if (dpi > 0)
                    {
                        return Math.Round(dpi / 96.0, 2);
                    }
                }
                finally
                {
                    ReleaseDC(monitor, monitorDc);
                }
            }
        }
        catch
        {
            // DPI detection is best-effort; callers treat 1.0 as the fallback.
        }

        return 1.0;
    }

    private const uint MonitorInfoPrimary = 1;

    private const int LogPixelsX = 88;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public uint Size;

        public RECT RcMonitor;

        public RECT RcWork;

        public uint DwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr hdcMonitor,
        ref RECT lprcMonitor,
        IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumProc proc,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEXW info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);
}
