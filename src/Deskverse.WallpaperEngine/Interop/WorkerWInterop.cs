namespace Deskverse.WallpaperEngine.Interop;

using System.Runtime.InteropServices;

/// <summary>
/// Desktop layer helpers. The WorkerW window sits between the desktop background
/// and the icons; parenting our video window to it renders video behind icons.
/// </summary>
internal static class WorkerWInterop
{
    /// <summary>Message sent to Progman to materialize the WorkerW child window.</summary>
    private const uint WallpaperLayerMessage = 0x052C;

    private const uint SmtoNormal = 0x0;

    private const int GwlStyle = -16;

    private const long WsVisible = 0x10000000L;

    private const long WsChild = 0x40000000L;

    /// <summary>Returns the WorkerW handle behind the desktop icons, creating it if necessary.</summary>
    public static IntPtr GetWorkerW()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        SendMessageTimeout(
            progman,
            WallpaperLayerMessage,
            UIntPtr.Zero,
            IntPtr.Zero,
            SmtoNormal,
            1000,
            out _);

        return FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
    }

    /// <summary>Re-parents a window under WorkerW and stretches it across the given rect.</summary>
    public static bool AttachToWorkerW(IntPtr hwnd, int x, int y, int width, int height)
    {
        var workerW = GetWorkerW();
        if (workerW == IntPtr.Zero)
        {
            return false;
        }

        // Remove WS_CHILD-adjacent top-level styling quirks, then re-parent.
        SetParent(hwnd, workerW);
        SetWindowLong(hwnd, GwlStyle, (int)(WsVisible | WsChild));
        return SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            SetWindowPosFlags.NoZOrder | SetWindowPosFlags.FrameChanged);
    }

    public static void DetachFromDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetParent(hwnd, IntPtr.Zero);
    }

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        NoZOrder = 0x0004,

        FrameChanged = 0x0020,
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        SetWindowPosFlags flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}
