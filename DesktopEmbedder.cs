using System.Runtime.InteropServices;

/// <summary>
/// Attaches the GLFW window to the Windows desktop layer below the icon view.
/// WorkerW is an Explorer implementation detail, so both current Windows 11
/// and the older, top-level WorkerW layout are supported here.
/// </summary>
public static class DesktopEmbedder
{
    private const uint WmSpawnWorkerW = 0x052C;
    private const uint SmtoNormal = 0x0000;

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsChild = 0x40000000;
    // WS_OVERLAPPEDWINDOW: caption, system menu, resize border and min/max buttons.
    // A desktop child must not retain any of those non-client decorations.
    private const long WsOverlappedWindow = 0x00CF0000;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoRedirectionBitmap = 0x00200000;
    private const uint LwaAlpha = 0x00000002;

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShow = 5;

    private static readonly IntPtr HwndBottom = new(1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte alpha, uint flags);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public static bool TryEmbed(IntPtr windowHandle, int maxAttempts = 20, int delayMs = 100)
    {
        if (windowHandle == IntPtr.Zero)
        {
            Console.WriteLine("[DesktopEmbedder] GLFW did not return a window handle.");
            return false;
        }

        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            Console.WriteLine($"[DesktopEmbedder] Progman was not found (Win32 error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        Console.WriteLine($"[DesktopEmbedder] Progman: {progman}");

        // Windows 11 needs this undocumented variant; older Explorer versions
        // use the first one. Sending both is harmless and covers both layouts.
        SendSpawnMessage(progman, 0xD, 0);
        SendSpawnMessage(progman, 0xD, 1);

        IntPtr workerW = IntPtr.Zero;
        for (int attempt = 1; attempt <= maxAttempts && workerW == IntPtr.Zero; attempt++)
        {
            workerW = FindDesktopWorkerW(progman);
            if (workerW == IntPtr.Zero && attempt < maxAttempts)
                Thread.Sleep(delayMs);
        }

        // A child WorkerW is the expected Windows 11 configuration. Older
        // Explorer builds expose the same layer as a top-level WorkerW.
        if (workerW != IntPtr.Zero)
        {
            Console.WriteLine($"[DesktopEmbedder] Desktop WorkerW: {workerW}");

            // Newer Windows 11 uses the "raised desktop". Progman itself has
            // no redirection bitmap and its icon view is layered. A child of
            // WorkerW is hidden by the system wallpaper in this arrangement.
            // Microsoft requires an opaque layered child of Progman, directly
            // below SHELLDLL_DefView and above WorkerW instead.
            if (IsRaisedDesktop(progman) && TryFindShellView(progman, out IntPtr shellView))
            {
                Console.WriteLine("[DesktopEmbedder] Raised desktop detected; creating a layered Progman child below the icon view.");
                return ReparentAndFit(windowHandle, progman, shellView, makeLayered: true);
            }

            return ReparentAndFit(windowHandle, workerW, IntPtr.Zero);
        }

        // On shell variants without a WorkerW, a child of Progman placed at
        // the very bottom is still below SHELLDLL_DefView (the desktop icons).
        Console.WriteLine("[DesktopEmbedder] WorkerW was not created; using Progman fallback.");
        return ReparentAndFit(windowHandle, progman, HwndBottom);
    }

    private static void SendSpawnMessage(IntPtr progman, int wParam, int lParam)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr sendResult = SendMessageTimeout(
            progman, WmSpawnWorkerW, new IntPtr(wParam), new IntPtr(lParam), SmtoNormal, 1000, out _);

        if (sendResult == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
            Console.WriteLine($"[DesktopEmbedder] WorkerW spawn request ({wParam}, {lParam}) timed out or failed: {Marshal.GetLastWin32Error()}.");
    }

    private static IntPtr FindDesktopWorkerW(IntPtr progman)
    {
        // Windows 11: WorkerW is a direct child of Progman. Ignore a WorkerW
        // that hosts the icon view; the other one is the wallpaper layer.
        IntPtr child = IntPtr.Zero;
        while ((child = FindWindowEx(progman, child, "WorkerW", null)) != IntPtr.Zero)
        {
            if (FindWindowEx(child, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                return child;
        }

        // Windows 7–10 classic layout: WorkerW follows the top-level window
        // whose child is SHELLDLL_DefView.
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                return true;

            IntPtr candidate = FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
            if (candidate != IntPtr.Zero)
            {
                result = candidate;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsRaisedDesktop(IntPtr progman)
    {
        Marshal.SetLastPInvokeError(0);
        long exStyle = GetWindowLongPtr(progman, GwlExStyle).ToInt64();
        return (exStyle & WsExNoRedirectionBitmap) != 0;
    }

    private static bool TryFindShellView(IntPtr progman, out IntPtr shellView)
    {
        shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shellView != IntPtr.Zero)
            return true;

        // Some Explorer revisions retain SHELLDLL_DefView inside a WorkerW.
        IntPtr child = IntPtr.Zero;
        while ((child = FindWindowEx(progman, child, "WorkerW", null)) != IntPtr.Zero)
        {
            shellView = FindWindowEx(child, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
                return true;
        }

        return false;
    }

    private static bool ReparentAndFit(IntPtr windowHandle, IntPtr parent, IntPtr insertAfter, bool makeLayered = false)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr originalStyle = GetWindowLongPtr(windowHandle, GwlStyle);
        if (originalStyle == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            Console.WriteLine($"[DesktopEmbedder] Could not read GLFW window style: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        long childStyle = (originalStyle.ToInt64() & ~(WsPopup | WsOverlappedWindow)) | WsChild;
        Marshal.SetLastPInvokeError(0);
        SetWindowLongPtr(windowHandle, GwlStyle, new IntPtr(childStyle));
        if (Marshal.GetLastWin32Error() != 0)
        {
            Console.WriteLine($"[DesktopEmbedder] Could not set WS_CHILD before SetParent: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        if (makeLayered && !MakeOpaqueLayered(windowHandle))
        {
            SetWindowLongPtr(windowHandle, GwlStyle, originalStyle);
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        IntPtr previousParent = SetParent(windowHandle, parent);
        int parentError = Marshal.GetLastWin32Error();
        if (previousParent == IntPtr.Zero && parentError != 0)
        {
            SetWindowLongPtr(windowHandle, GwlStyle, originalStyle);
            Console.WriteLine($"[DesktopEmbedder] SetParent failed: {parentError}.");
            return false;
        }

        if (!GetClientRect(parent, out Rect bounds) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            Console.WriteLine($"[DesktopEmbedder] Could not get desktop host bounds: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        uint flags = SwpFrameChanged | SwpNoActivate | SwpShowWindow;
        if (!SetWindowPos(windowHandle, insertAfter, 0, 0, bounds.Width, bounds.Height, flags))
        {
            Console.WriteLine($"[DesktopEmbedder] SetWindowPos failed: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        ShowWindow(windowHandle, SwShow);
        Console.WriteLine($"[DesktopEmbedder] Attached to {parent}; size {bounds.Width}x{bounds.Height}.");
        return true;
    }

    private static bool MakeOpaqueLayered(IntPtr windowHandle)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr originalExStyle = GetWindowLongPtr(windowHandle, GwlExStyle);
        if (originalExStyle == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            Console.WriteLine($"[DesktopEmbedder] Could not read GLFW extended style: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        SetWindowLongPtr(windowHandle, GwlExStyle, new IntPtr(originalExStyle.ToInt64() | WsExLayered));
        if (Marshal.GetLastWin32Error() != 0)
        {
            Console.WriteLine($"[DesktopEmbedder] Could not set WS_EX_LAYERED: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        if (!SetLayeredWindowAttributes(windowHandle, 0, 255, LwaAlpha))
        {
            Console.WriteLine($"[DesktopEmbedder] Could not set layered-window opacity: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        return true;
    }
}
