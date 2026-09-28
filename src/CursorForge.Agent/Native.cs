using System.Runtime.InteropServices;

namespace CursorForge.Agent;

internal static unsafe partial class Native
{
    // ---- messages ----
    public const uint WM_NULL = 0x0000, WM_DESTROY = 0x0002, WM_SETTINGCHANGE = 0x001A, WM_DISPLAYCHANGE = 0x007E,
        WM_NCHITTEST = 0x0084, WM_TIMER = 0x0113, WM_HOTKEY = 0x0312, WM_POWERBROADCAST = 0x0218,
        WM_WTSSESSION_CHANGE = 0x02B1, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B,
        WM_DPICHANGED = 0x02E0;

    public const uint WTS_SESSION_UNLOCK = 0x8, PBT_APMRESUMEAUTOMATIC = 0x12, SPI_SETCURSORS = 0x0057;

    // ---- window styles ----
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
        WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
        SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200, SWP_NOSENDCHANGING = 0x400;
    public static readonly nint HWND_TOPMOST = -1;
    public const int SW_HIDE = 0;
    public const nint HTTRANSPARENT = -1;

    public const uint PM_REMOVE = 1, QS_ALLINPUT = 0x04FF, MWMO_INPUTAVAILABLE = 0x4, INFINITE = 0xFFFFFFFF;
    public const uint CURSOR_SHOWING = 1;
    public const uint ULW_ALPHA = 2;
    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

    public const uint MOD_NOREPEAT = 0x4000;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;

    public const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_CHECKED = 0x8;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    public const uint NIIF_INFO = 1, NIIF_NOSOUND = 0x10;

    public const uint TH32CS_SNAPPROCESS = 2;
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // ---- structs ----
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO
    {
        public uint cbSize, flags;
        public nint hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot, yHotspot;
        public nint hbmMask, hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    // ---- user32 ----
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")] public static partial ushort RegisterClassEx(WNDCLASSEXW* wc);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] public static partial int DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")] public static partial nint DefWindowProc(nint hwnd, uint msg, nint w, nint l);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] public static partial int GetMessage(MSG* msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")] public static partial int PeekMessage(MSG* msg, nint hwnd, uint min, uint max, uint remove);
    [LibraryImport("user32.dll")] public static partial int TranslateMessage(MSG* msg);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] public static partial nint DispatchMessage(MSG* msg);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")] public static partial int PostMessage(nint hwnd, uint msg, nint w, nint l);
    [LibraryImport("user32.dll")] public static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] public static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint ms, uint wakeMask, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindow(string className, string? windowName);
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")] public static partial int SetSystemCursor(nint cursor, uint id);
    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] public static partial nint CreateIconIndirect(ICONINFO* info);
    [LibraryImport("user32.dll")] public static partial int DestroyCursor(nint cursor);
    [LibraryImport("user32.dll")] public static partial int DestroyIcon(nint icon);
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static partial int SystemParametersInfo(uint action, uint uiParam, nint pv, uint winIni);
    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")] public static partial nint LoadCursor(nint instance, nint name);
    [LibraryImport("user32.dll")] public static partial int GetCursorInfo(CURSORINFO* info);
    [LibraryImport("user32.dll")] public static partial int GetCursorPos(POINT* p);
    [LibraryImport("user32.dll")] public static partial int GetClipCursor(RECT* r);
    [LibraryImport("user32.dll")] public static partial int GetWindowRect(nint hwnd, RECT* r);

    [LibraryImport("user32.dll")] public static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] public static partial int ShowWindow(nint hwnd, int cmd);
    [LibraryImport("user32.dll")] public static partial int UpdateLayeredWindow(nint hwnd, nint hdcDst, POINT* ptDst, SIZE* size,
        nint hdcSrc, POINT* ptSrc, uint key, BLENDFUNCTION* blend, uint flags);
    [LibraryImport("user32.dll")] public static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int ReleaseDC(nint hwnd, nint hdc);
    [LibraryImport("user32.dll")] public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] public static partial int SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(nint hwnd, uint* pid);
    [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")] public static partial int SetProcessDpiAwarenessContext(nint ctx);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint min, uint max, nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> proc, uint pid, uint tid, uint flags);
    [LibraryImport("user32.dll")] public static partial int UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")] public static partial int RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [LibraryImport("user32.dll")] public static partial int UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll")] public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [LibraryImport("user32.dll")] public static partial int KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")] public static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AppendMenu(nint menu, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")] public static partial int SetMenuDefaultItem(nint menu, uint item, uint byPos);
    [LibraryImport("user32.dll")] public static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [LibraryImport("user32.dll")] public static partial int DestroyMenu(nint menu);

    // ---- gdi32 ----
    [LibraryImport("gdi32.dll")] public static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* bmi, uint usage, void** bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")] public static partial nint CreateBitmap(int w, int h, uint planes, uint bpp, void* bits);
    [LibraryImport("gdi32.dll")] public static partial int DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleDC(nint hdc);
    [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint hdc, nint obj);
    [LibraryImport("gdi32.dll")] public static partial int DeleteDC(nint hdc);

    // ---- shell32 / kernel32 / dwm / wts ----
    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] public static partial int Shell_NotifyIcon(uint msg, NOTIFYICONDATAW* data);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")] public static partial nint GetModuleHandle(nint name);
    [LibraryImport("kernel32.dll")] public static partial nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW")] public static partial int Process32First(nint snap, PROCESSENTRY32W* e);
    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW")] public static partial int Process32Next(nint snap, PROCESSENTRY32W* e);
    [LibraryImport("kernel32.dll")] public static partial int CloseHandle(nint h);
    [LibraryImport("dwmapi.dll")] public static partial int DwmFlush();

    // ---- input state (click flash) ----
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [LibraryImport("user32.dll")] public static partial short GetAsyncKeyState(int vk);
    [LibraryImport("user32.dll")] public static partial int GetLastInputInfo(LASTINPUTINFO* info);
    [LibraryImport("kernel32.dll")] public static partial uint WaitForMultipleObjects(uint count, nint* handles, int waitAll, uint ms);
    [LibraryImport("dwmapi.dll")] public static partial int DwmGetCompositionTimingInfo(nint hwnd, byte* info);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW")] public static partial nint CreateWaitableTimerEx(nint attrs, nint name, uint flags, uint access);
    [LibraryImport("kernel32.dll")] public static partial int SetWaitableTimer(nint timer, long* dueTime, int period, nint completion, nint arg, int resume);
    [LibraryImport("kernel32.dll")] public static partial uint WaitForSingleObject(nint handle, uint ms);
    [LibraryImport("wtsapi32.dll")] public static partial int WTSRegisterSessionNotification(nint hwnd, uint flags);
}
