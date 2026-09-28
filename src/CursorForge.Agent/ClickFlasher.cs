using System.Diagnostics;
using static CursorForge.Agent.Native;

namespace CursorForge.Agent;

/// <summary>
/// Recolors the cursor while a mouse button is down.
///
/// Button state is polled (GetAsyncKeyState) rather than received per event: Raw Input or a mouse hook
/// would wake us for every single movement report (1000-8000 per second on gaming mice), while polling
/// costs the same tiny amount at any mouse rate. It polls every 8 ms while the mouse is in use and every
/// 100 ms when the user is idle, and nothing at all while the feature is off. Nothing is hooked, so input
/// latency is untouched.
///
/// The flash swaps in pre-rendered multi-resolution cursor files loaded with LR_DEFAULTSIZE (Windows picks
/// the frame for its pointer size, so the flash is as sharp as the normal cursor); the normal scheme is
/// reloaded afterwards. Nothing is swapped when the pointer isn't showing one of our cursors (e.g. in games).
/// </summary>
internal sealed unsafe class ClickFlasher
{
    const uint IMAGE_CURSOR = 2, LR_LOADFROMFILE = 0x10, LR_DEFAULTSIZE = 0x40;
    const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, SM_SWAPBUTTON = 23;
    const int ActiveIntervalMs = 8, IdleIntervalMs = 100, IdleAfterMs = 1500;

    sealed record Settings(string Dir, (CursorRole Role, uint Id)[] Roles, nint[] Known, int DurationMs);

    readonly AutoResetEvent _wake = new(false);
    volatile Settings? _settings;
    volatile bool _stop;
    Thread? _thread;
    nint _timer;

    // poll-thread state
    bool _flashing, _wasLeft, _wasRight;
    long _flashStart;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "CursorForge click flash" };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(1000);
    }

    /// <summary>Pass a null dir to switch the feature off.</summary>
    public void Configure(string? dir, IEnumerable<(CursorRole Role, uint Id)> roles, nint[] known, int durationMs)
    {
        _settings = dir == null ? null
            : new Settings(dir, roles.Where(r => !CursorRenderer.IsAnimated(r.Role)).ToArray(), known, durationMs);
        _wake.Set();
    }

    void Run()
    {
        _timer = CreateWaitableTimerEx(0, 0, 0x2 /* HIGH_RESOLUTION */, 0x1F0003);
        nint wake = _wake.SafeWaitHandle.DangerousGetHandle();
        while (!_stop)
        {
            var s = _settings;
            if (s == null)
            {
                if (_flashing) EndFlash();
                _wake.WaitOne(); // feature off: sleep until reconfigured
                continue;
            }

            Sleep(UserIsActive() ? ActiveIntervalMs : IdleIntervalMs, wake);
            if (_stop) break;
            s = _settings;
            if (s == null) continue;
            Poll(s);
        }
        if (_flashing) EndFlash();
        if (_timer != 0) CloseHandle(_timer);
    }

    void Poll(Settings s)
    {
        bool left = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        bool right = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        if (GetSystemMetrics(SM_SWAPBUTTON) != 0) (left, right) = (right, left); // async state is physical

        bool pressed = (left && !_wasLeft) || (right && !_wasRight);
        bool rightOnly = right && !_wasRight && !(left && !_wasLeft);
        _wasLeft = left;
        _wasRight = right;

        if (pressed && (_flashing || PointerIsOurs(s)))
        {
            BeginFlash(s, rightOnly);
        }
        else if (_flashing && !left && !right)
        {
            long elapsedMs = (Stopwatch.GetTimestamp() - _flashStart) * 1000 / Stopwatch.Frequency;
            if (elapsedMs >= s.DurationMs) EndFlash();
        }
    }

    static bool UserIsActive()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        return GetLastInputInfo(&info) == 0 || unchecked((uint)Environment.TickCount - info.dwTime) < IdleAfterMs;
    }

    void Sleep(int ms, nint wake)
    {
        long due = -ms * 10_000L;
        if (_timer != 0 && SetWaitableTimer(_timer, &due, 0, 0, 0, 0) != 0)
        {
            nint* handles = stackalloc nint[2] { _timer, wake };
            WaitForMultipleObjects(2, handles, 0, (uint)ms + 50);
        }
        else _wake.WaitOne(ms);
    }

    static bool PointerIsOurs(Settings s)
    {
        var ci = new CURSORINFO { cbSize = (uint)sizeof(CURSORINFO) };
        return GetCursorInfo(&ci) != 0 && (ci.flags & CURSOR_SHOWING) != 0 && Array.IndexOf(s.Known, ci.hCursor) >= 0;
    }

    void BeginFlash(Settings s, bool right)
    {
        foreach (var (role, id) in s.Roles)
        {
            nint h = LoadImage(0, SchemeCursors.FlashPath(s.Dir, role, right), IMAGE_CURSOR, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (h != 0 && SetSystemCursor(h, id) == 0) DestroyCursor(h);
        }
        _flashing = true;
        _flashStart = Stopwatch.GetTimestamp();
    }

    void EndFlash()
    {
        _flashing = false;
        SystemParametersInfo(SPI_SETCURSORS, 0, 0, 0); // back to the normal (scheme) cursors
    }
}
