using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FolderChat.Web.Infrastructure;

/// <summary>
/// Windows' own "Select Folder" window, the one File Explorer uses. The FolderChat server opens it on behalf of the
/// browser on the same computer, over that browser's window.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsFolderDialog
{
    /// <summary>
    /// Shows the window and waits for the user. Null when they cancel, or when <paramref name="ct"/> is cancelled, which
    /// closes the window.
    /// </summary>
    public static async Task<string?> ShowAsync(string title, CancellationToken ct)
    {
        var chosen = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        uint threadId = 0;

        // The dialog is a COM object that needs a single-threaded apartment, which request threads aren't.
        var thread = new Thread(() =>
        {
            Volatile.Write(ref threadId, GetCurrentThreadId());
            try
            {
                chosen.TrySetResult(ct.IsCancellationRequested ? null : Show(title));
            }
            catch (Exception ex)
            {
                chosen.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Folder window",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // The page was closed or reloaded: close the window too, rather than leave it waiting for nobody.
        using var closeOnCancel = ct.Register(() => CloseDialogs(Volatile.Read(ref threadId)));
        try
        {
            var path = await chosen.Task;
            return ct.IsCancellationRequested ? null : path;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string? Show(string title)
    {
        DrawForScreenPixels();

        // The browser the user just clicked in, which the window opens over.
        var browser = GetForegroundWindow();
        var area = browser != 0 && GetWindowRect(browser, out var rect) ? rect : (Rect?)null;

        // Windows opens the window in a corner of the screen, then grows it to the size it had last time. Center it over
        // the browser each time its size changes in its first moments, before the user could be resizing it.
        var opened = 0L;
        var centeredSize = (Width: 0, Height: 0);
        HookCallback centerOnBrowser = (code, wParam, lParam) =>
        {
            if (code >= 0 && area is { } bounds && Marshal.PtrToStructure<HandledMessage>(lParam) is { Message: PositionChanged } handled
                && IsDialog(handled.Window) && GetWindowRect(handled.Window, out var now))
            {
                opened = opened == 0 ? Environment.TickCount64 : opened;
                var size = (Width: now.Right - now.Left, Height: now.Bottom - now.Top);
                if (size != centeredSize && Environment.TickCount64 - opened < SettlingMilliseconds)
                {
                    centeredSize = size;
                    Center(handled.Window, bounds);
                }
            }

            return CallNextHookEx(0, code, wParam, lParam);
        };
        var hook = SetWindowsHookExW(AfterMessages, centerOnBrowser, 0, GetCurrentThreadId());

        var owner = CreateOwner(area ?? default);
        var dialog = (IFileDialog)new FileOpenDialog();
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | PickFolders | ForceFileSystem | PathMustExist | NoChangeDirectory);
            dialog.SetTitle(title);

            var result = dialog.Show(owner);
            if (result == Cancelled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(FileSystemPath, out var path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
            if (owner != 0)
            {
                DestroyWindow(owner);
            }

            if (hook != 0)
            {
                UnhookWindowsHookEx(hook);
            }

            GC.KeepAlive(centerOnBrowser);
        }
    }

    /// <summary>Keeps the window sharp on high-DPI screens, rather than stretched.</summary>
    private static void DrawForScreenPixels()
    {
        try
        {
            SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch (EntryPointNotFoundException)
        {
            // Windows before 10 version 1607 stretches the window instead.
        }
    }

    /// <summary>
    /// An invisible, always-on-top window over the browser. Owned by it, the dialog stays in front of the browser even
    /// when Windows won't let it take the focus, because the FolderChat server isn't the app the user is using.
    /// </summary>
    private static nint CreateOwner(Rect browser) =>
        CreateWindowExW(Topmost | ToolWindow, "STATIC", null, Popup, browser.Left, browser.Top, 0, 0, 0, 0, 0, 0);

    /// <summary>Moves the window to the middle of <paramref name="area"/>, kept inside that screen's work area.</summary>
    private static void Center(nint window, Rect area)
    {
        var screen = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(window, out var size) || !GetMonitorInfoW(MonitorFromRect(ref area, NearestMonitor), ref screen))
        {
            return;
        }

        var (width, height) = (size.Right - size.Left, size.Bottom - size.Top);
        var work = screen.Work;
        var x = Math.Clamp((area.Left + area.Right - width) / 2, work.Left, Math.Max(work.Left, work.Right - width));
        var y = Math.Clamp((area.Top + area.Bottom - height) / 2, work.Top, Math.Max(work.Top, work.Bottom - height));
        SetWindowPos(window, 0, x, y, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    /// <summary>Closes the dialog showing on that thread, as its Cancel button would.</summary>
    private static void CloseDialogs(uint threadId)
    {
        if (threadId == 0)
        {
            return;
        }

        EnumThreadWindows(threadId, (window, _) =>
        {
            if (IsDialog(window))
            {
                PostMessageW(window, WindowClose, 0, 0);
            }

            return true;
        }, 0);
    }

    private static bool IsDialog(nint window)
    {
        var name = new char[8];
        return new string(name, 0, GetClassNameW(window, name, name.Length)) == DialogClass;
    }

    private const uint NoChangeDirectory = 0x8, PickFolders = 0x20, ForceFileSystem = 0x40, PathMustExist = 0x800;
    private const uint FileSystemPath = 0x80058000; // SIGDN_FILESYSPATH
    private const int Cancelled = unchecked((int)0x800704C7); // HRESULT_FROM_WIN32(ERROR_CANCELLED)
    private const int Topmost = 0x8, ToolWindow = 0x80; // WS_EX_TOPMOST, WS_EX_TOOLWINDOW
    private const int Popup = unchecked((int)0x80000000); // WS_POPUP
    private const uint WindowClose = 0x10; // WM_CLOSE
    private const string DialogClass = "#32770";
    private const nint PerMonitorAwareV2 = -4; // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private const int AfterMessages = 12; // WH_CALLWNDPROCRET
    private const uint PositionChanged = 0x47; // WM_WINDOWPOSCHANGED
    private const long SettlingMilliseconds = 1000;
    private const uint NoSize = 0x1, NoZOrder = 0x4, NoActivate = 0x10; // SWP_*
    private const uint NearestMonitor = 2; // MONITOR_DEFAULTTONEAREST

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>CWPRETSTRUCT: a message a window has just handled.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct HandledMessage
    {
        public nint Result, LParam, WParam;
        public uint Message;
        public nint Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    private delegate nint HookCallback(int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SetWindowsHookExW(int kind, HookCallback callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        int extendedStyle, string className, string? windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, WindowCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, char[] name, int length);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    /// <summary>IFileDialog, declared up to the last method used. COM calls go by position, so the order matters.</summary>
    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(nint owner);

        void SetFileTypes(uint count, nint filters);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(nint events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem folder);

        void SetFolder(IShellItem folder);

        void GetFolder(out IShellItem folder);

        void GetCurrentSelection(out IShellItem item);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid interfaceId, out nint result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint form, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }
}
