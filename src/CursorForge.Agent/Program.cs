using System.Diagnostics;
using System.Runtime.InteropServices;
using static CursorForge.Agent.Native;

namespace CursorForge.Agent;

/// <summary>
/// The background agent: a hidden window + message loop. Idle cost is zero - it only wakes up for
/// config reloads, hotkeys, tray clicks, foreground-window changes and the rare OS event that
/// resets the cursors (settings change, unlock, resume, display change).
/// </summary>
internal static unsafe class Program
{
    const int HotkeyToggle = 1, HotkeyOverlay = 2;
    const nuint TimerReapply = 1, TimerTrayRetry = 2;
    const uint MenuOpen = 1, MenuEnabled = 2, MenuExit = 3;

    static AppConfig _cfg = new();
    static nint _hwnd, _winEventHook;
    static Overlay _overlay = null!;
    static Tray _tray = null!;
    static uint _taskbarCreated;
    static long _lastApplyTick;
    static int _trayRetries;

    static int Main(string[] args)
    {
        bool exitRequest = args.Contains("--exit", StringComparer.OrdinalIgnoreCase);
        using var mutex = new Mutex(true, Ipc.AgentMutex, out bool owned);
        if (!owned)
        {
            nint other = FindWindow(Ipc.AgentWindowClass, null);
            if (other != 0) PostMessage(other, exitRequest ? Ipc.MsgExit : Ipc.MsgReload, 0, 0);
            if (exitRequest)
            {
                try { if (mutex.WaitOne(3000)) mutex.ReleaseMutex(); }
                catch (AbandonedMutexException) { }
            }
            return 0;
        }
        if (exitRequest)
        {
            // Not running: still undo our cursor scheme (used by the uninstaller).
            SchemeCursors.Restore();
            mutex.ReleaseMutex();
            return 0;
        }

        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        _cfg = ConfigStore.Load();
        nint inst = GetModuleHandle(0);
        fixed (char* cls = Ipc.AgentWindowClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = inst,
                lpszClassName = cls,
            };
            RegisterClassEx(&wc);
        }
        // A hidden top-level window (not message-only) so we receive TaskbarCreated and WM_SETTINGCHANGE broadcasts.
        _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW, Ipc.AgentWindowClass, "CursorForge Agent", WS_POPUP, 0, 0, 0, 0, 0, 0, inst, 0);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        WTSRegisterSessionNotification(_hwnd, 0);

        _overlay = new Overlay();
        _overlay.Start();
        _tray = new Tray(_hwnd);
        _winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &OnWinEvent, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        ApplyAll();
        if (!_tray.Added) SetTimer(_hwnd, TimerTrayRetry, 2000, 0);

        MSG msg;
        while (GetMessage(&msg, 0, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }

        if (_winEventHook != 0) UnhookWinEvent(_winEventHook);
        _overlay.Stop();
        _tray.Remove();
        SchemeCursors.Restore();
        DestroyWindow(_hwnd);
        mutex.ReleaseMutex();
        return 0;
    }

    static void ApplyAll()
    {
        RegisterHotkeys();
        RenderedCursor? rc = _cfg.Enabled ? RenderSafe() : null;
        ApplyCursors(rc);
        _overlay.SetImage(rc);
        int iconPx = Math.Clamp(GetSystemMetrics(49 /* SM_CXSMICON */), 16, 64);
        _tray.Update(CursorRenderer.RenderIcon(_cfg.Style, iconPx),
            _cfg.Enabled ? "CursorForge" : "CursorForge (paused)");
        UpdateForeground(GetForegroundWindow());
    }

    static RenderedCursor RenderSafe()
    {
        // Same size as the scheme cursors, so the game overlay matches the desktop cursor.
        try { return CursorRenderer.Render(CursorRenderer.Effective(_cfg.Style, WindowsPointer.BaseSize())); }
        catch { return CursorRenderer.Render(Presets.Default.Style); }
    }

    static void ApplyCursors(RenderedCursor? rc)
    {
        if (rc == null)
        {
            SchemeCursors.Restore();
        }
        else
        {
            try
            {
                SchemeCursors.Apply(_cfg);
            }
            catch
            {
                // Registry/file trouble: fall back to runtime cursors (work everywhere, but Windows may scale them).
                SystemCursors.Restore();
                SystemCursors.Apply(_cfg, rc);
            }
        }
        _overlay.SetSystemHandles(SystemCursors.KnownHandles());
        _lastApplyTick = Environment.TickCount64;
    }

    static void ScheduleReapply() => SetTimer(_hwnd, TimerReapply, 400, 0);

    static void RegisterHotkeys()
    {
        UnregisterHotKey(_hwnd, HotkeyToggle);
        UnregisterHotKey(_hwnd, HotkeyOverlay);
        if (_cfg.ToggleHotkey.Key != 0)
            RegisterHotKey(_hwnd, HotkeyToggle, _cfg.ToggleHotkey.Modifiers | MOD_NOREPEAT, _cfg.ToggleHotkey.Key);
        if (_cfg.OverlayHotkey.Key != 0)
            RegisterHotKey(_hwnd, HotkeyOverlay, _cfg.OverlayHotkey.Modifiers | MOD_NOREPEAT, _cfg.OverlayHotkey.Key);
    }

    static void UpdateForeground(nint hwnd)
    {
        if (!_cfg.Enabled || !_cfg.OverlayEnabled || _cfg.OverlayApps.Count == 0 || hwnd == 0)
        {
            _overlay.SetTarget(0, OverlayMode.Auto, false);
            return;
        }
        string? name = ProcessName(hwnd);
        var app = name == null ? null : _cfg.OverlayApps.FirstOrDefault(a => a.Matches(name));
        _overlay.SetTarget(hwnd, app?.Mode ?? OverlayMode.Auto, app != null);
    }

    /// <summary>
    /// Resolves a window's exe name from a Toolhelp snapshot, so we never open a handle to the
    /// (possibly anti-cheat protected) game process. Only runs on foreground changes.
    /// </summary>
    static string? ProcessName(nint hwnd)
    {
        uint pid;
        GetWindowThreadProcessId(hwnd, &pid);
        if (pid == 0) return null;
        nint snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == -1 || snap == 0) return null;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            if (Process32First(snap, &e) == 0) return null;
            do
            {
                if (e.th32ProcessID == pid) return new string(e.szExeFile);
            } while (Process32Next(snap, &e) != 0);
            return null;
        }
        finally
        {
            CloseHandle(snap);
        }
    }

    static void ToggleEnabled()
    {
        _cfg = ConfigStore.Load();
        _cfg.Enabled = !_cfg.Enabled;
        ConfigStore.Save(_cfg);
        ApplyAll();
    }

    static void ToggleOverlayForForeground()
    {
        nint fg = GetForegroundWindow();
        uint pid;
        GetWindowThreadProcessId(fg, &pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return;
        string? exe = ProcessName(fg);
        if (exe == null) return;
        string name = OverlayApp.Normalize(exe);

        _cfg = ConfigStore.Load();
        var existing = _cfg.OverlayApps.FirstOrDefault(a => a.Matches(name));
        if (existing != null)
        {
            _cfg.OverlayApps.Remove(existing);
            _tray.Balloon("Overlay off", $"{name}.exe uses its own cursor again.");
        }
        else
        {
            _cfg.OverlayApps.Add(new OverlayApp { Process = name });
            _cfg.OverlayEnabled = true;
            _tray.Balloon("Overlay on", $"CursorForge will draw your cursor in {name}.exe.");
        }
        ConfigStore.Save(_cfg);
        UpdateForeground(fg);
    }

    static void OpenUi()
    {
        string path = Path.Combine(AppContext.BaseDirectory, Ipc.UiExe);
        if (!File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch { }
    }

    static void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, MenuOpen, "Open CursorForge");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING | (_cfg.Enabled ? MF_CHECKED : 0), MenuEnabled, "Enabled");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, MenuExit, "Exit (restore Windows cursors)");
        SetMenuDefaultItem(menu, MenuOpen, 0);

        POINT p;
        GetCursorPos(&p);
        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, p.X, p.Y, 0, _hwnd, 0);
        PostMessage(_hwnd, WM_NULL, 0, 0);
        DestroyMenu(menu);

        switch ((uint)cmd)
        {
            case MenuOpen: OpenUi(); break;
            case MenuEnabled: ToggleEnabled(); break;
            case MenuExit: PostQuitMessage(0); break;
        }
    }

    [UnmanagedCallersOnly]
    static void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { UpdateForeground(hwnd); }
        catch { }
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint w, nint l)
    {
        try
        {
            switch (msg)
            {
                case Ipc.MsgReload:
                    _cfg = ConfigStore.Load();
                    ApplyAll();
                    return 0;

                case Ipc.MsgExit:
                    PostQuitMessage(0);
                    return 0;

                case WM_HOTKEY:
                    if (w == HotkeyToggle) ToggleEnabled();
                    else if (w == HotkeyOverlay) ToggleOverlayForForeground();
                    return 0;

                case Tray.CallbackMessage:
                    uint mouse = (uint)(l & 0xFFFF);
                    if (mouse == WM_LBUTTONUP) OpenUi();
                    else if (mouse is WM_RBUTTONUP or WM_CONTEXTMENU) ShowMenu();
                    return 0;

                case WM_TIMER:
                    if (w == (nint)TimerReapply)
                    {
                        KillTimer(hwnd, TimerReapply);
                        if (_cfg.Enabled) ApplyCursors(RenderSafe());
                    }
                    else if (w == (nint)TimerTrayRetry)
                    {
                        _tray.Readd();
                        if (_tray.Added || ++_trayRetries > 30) KillTimer(hwnd, TimerTrayRetry);
                    }
                    return 0;

                case WM_SETTINGCHANGE:
                    // Windows reloaded the cursor scheme (Settings app, accessibility size, etc.) -> put ours back.
                    if ((uint)w == SPI_SETCURSORS && Environment.TickCount64 - _lastApplyTick > 1000) ScheduleReapply();
                    break;

                case WM_DISPLAYCHANGE:
                case WM_DPICHANGED:
                    ScheduleReapply();
                    break;

                case WM_WTSSESSION_CHANGE:
                    if ((uint)w == WTS_SESSION_UNLOCK) ScheduleReapply();
                    break;

                case WM_POWERBROADCAST:
                    if ((uint)w == PBT_APMRESUMEAUTOMATIC) ScheduleReapply();
                    break;

                default:
                    if (msg == _taskbarCreated && _taskbarCreated != 0)
                    {
                        _tray.Readd();
                        return 0;
                    }
                    break;
            }
        }
        catch
        {
            // Never let an exception cross the native boundary.
        }
        return DefWindowProc(hwnd, msg, w, l);
    }
}
