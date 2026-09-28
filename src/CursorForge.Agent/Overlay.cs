using System.Runtime.InteropServices;
using static CursorForge.Agent.Native;

namespace CursorForge.Agent;

/// <summary>
/// Draws the cursor as a click-through layered window for apps that use their own cursor.
/// No injection, no input hooks, no handles to the target process: it only reads the global
/// cursor state (GetCursorInfo), exactly like screen recorders do.
///
/// Cost model: while no listed app is focused the thread sleeps on an event (0% CPU). While one is
/// focused it polls ~60x/s; while the overlay is visible it follows the cursor once per DWM frame.
/// </summary>
internal sealed unsafe class Overlay
{
    const string WindowClass = "CursorForge.Overlay";

    readonly AutoResetEvent _wake = new(false);
    readonly Lock _lock = new();
    Thread? _thread;
    nint _wakeHandle;

    // written by the main thread
    volatile bool _active, _stop;
    volatile OverlayMode _mode;
    volatile nint _target;
    nint[] _systemHandles = [];
    RenderedCursor? _pendingImage;
    bool _imageDirty;

    // overlay-thread state
    nint _hwnd;
    bool _hasImage, _visible;
    int _hotX, _hotY, _lastX = int.MinValue, _lastY = int.MinValue, _pinned;
    long _lastTopmost;

    public void Start()
    {
        _wakeHandle = _wake.SafeWaitHandle.DangerousGetHandle();
        _thread = new Thread(Run) { IsBackground = true, Name = "CursorForge overlay", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(1000);
    }

    public void SetImage(RenderedCursor? rc)
    {
        lock (_lock)
        {
            _pendingImage = rc;
            _imageDirty = true;
        }
        _wake.Set();
    }

    public void SetSystemHandles(nint[] handles) => Volatile.Write(ref _systemHandles, handles);

    public void SetTarget(nint hwnd, OverlayMode mode, bool active)
    {
        _target = hwnd;
        _mode = mode;
        _active = active;
        _pinned = 0;
        _wake.Set();
    }

    void Run()
    {
        nint inst = GetModuleHandle(0);
        fixed (char* cls = WindowClass)
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
        _hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            WindowClass, "CursorForge Overlay", WS_POPUP, 0, 0, 1, 1, 0, 0, inst, 0);

        MSG msg;
        while (!_stop)
        {
            while (PeekMessage(&msg, 0, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(&msg);
                DispatchMessage(&msg);
            }
            if (_imageDirty) UpdateImage();

            if (!_active || !_hasImage)
            {
                Hide();
                Wait(INFINITE);
                continue;
            }

            // Visible: pace to the compositor so we move exactly once per frame, right after it presents.
            // Hidden: cheap ~60 Hz poll so we notice when the game shows its cursor again.
            if (!_visible || DwmFlush() != 0) Wait(_visible ? 4u : 10u);
            if (!_active || _stop) continue;

            if (ShouldShow(out int x, out int y)) Show(x, y);
            else Hide();
        }
        if (_hwnd != 0) DestroyWindow(_hwnd);
    }

    void Wait(uint ms)
    {
        nint h = _wakeHandle;
        MsgWaitForMultipleObjectsEx(1, &h, ms, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }

    bool ShouldShow(out int x, out int y)
    {
        var ci = new CURSORINFO { cbSize = (uint)sizeof(CURSORINFO) };
        if (GetCursorInfo(&ci) == 0) { x = y = 0; return false; }
        x = ci.ptScreenPos.X;
        y = ci.ptScreenPos.Y;

        bool showing = (ci.flags & CURSOR_SHOWING) != 0 && ci.hCursor != 0;
        // A standard cursor is already replaced system-wide - never double it.
        if (showing && Array.IndexOf(Volatile.Read(ref _systemHandles), ci.hCursor) >= 0) return false;
        if (_mode == OverlayMode.Auto) return showing;

        // "Always": the game may hide the OS cursor and draw its own. Stay hidden while the cursor
        // is locked (mouse-look / aiming), detected via a tiny clip rect or a cursor pinned to the centre.
        RECT clip;
        if (GetClipCursor(&clip) != 0 && (clip.Right - clip.Left < 64 || clip.Bottom - clip.Top < 64)) return false;
        if (showing) { _pinned = 0; return true; }

        RECT wr;
        if (GetWindowRect(_target, &wr) == 0) return false;
        if (x < wr.Left || x >= wr.Right || y < wr.Top || y >= wr.Bottom) return false;
        int cx = (wr.Left + wr.Right) / 2, cy = (wr.Top + wr.Bottom) / 2;
        if (Math.Abs(x - cx) <= 2 && Math.Abs(y - cy) <= 2)
        {
            if (++_pinned >= 2) return false;
        }
        else _pinned = 0;
        return true;
    }

    void Show(int x, int y)
    {
        int wx = x - _hotX, wy = y - _hotY;
        long now = Environment.TickCount64;
        if (!_visible)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, wx, wy, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _visible = true;
            _lastTopmost = now;
        }
        else if (now - _lastTopmost > 1000)
        {
            // Borderless games sometimes re-assert their own topmost state.
            SetWindowPos(_hwnd, HWND_TOPMOST, wx, wy, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            _lastTopmost = now;
        }
        else if (x != _lastX || y != _lastY)
        {
            SetWindowPos(_hwnd, 0, wx, wy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
        }
        _lastX = x;
        _lastY = y;
    }

    void Hide()
    {
        if (!_visible) return;
        ShowWindow(_hwnd, SW_HIDE);
        _visible = false;
    }

    void UpdateImage()
    {
        RenderedCursor? rc;
        lock (_lock)
        {
            rc = _pendingImage;
            _imageDirty = false;
        }
        if (rc == null)
        {
            _hasImage = false;
            Hide();
            return;
        }

        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = rc.Width,
            biHeight = -rc.Height,
            biPlanes = 1,
            biBitCount = 32,
        };
        nint screen = GetDC(0);
        void* bits;
        nint bmp = CreateDIBSection(screen, &bi, 0, &bits, 0, 0);
        if (bmp == 0) { ReleaseDC(0, screen); return; }

        // UpdateLayeredWindow wants premultiplied alpha.
        byte* dst = (byte*)bits;
        var src = rc.Pixels;
        for (int i = 0; i < src.Length; i += 4)
        {
            int a = src[i + 3];
            dst[i + 0] = (byte)((src[i + 0] * a + 127) / 255);
            dst[i + 1] = (byte)((src[i + 1] * a + 127) / 255);
            dst[i + 2] = (byte)((src[i + 2] * a + 127) / 255);
            dst[i + 3] = (byte)a;
        }

        nint mem = CreateCompatibleDC(screen);
        nint old = SelectObject(mem, bmp);
        var size = new SIZE { cx = rc.Width, cy = rc.Height };
        var srcPt = new POINT();
        var dstPt = new POINT { X = _lastX == int.MinValue ? 0 : _lastX - rc.HotX, Y = _lastY == int.MinValue ? 0 : _lastY - rc.HotY };
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(_hwnd, screen, &dstPt, &size, mem, &srcPt, 0, &blend, ULW_ALPHA);
        SelectObject(mem, old);
        DeleteDC(mem);
        DeleteObject(bmp);
        ReleaseDC(0, screen);

        _hotX = rc.HotX;
        _hotY = rc.HotY;
        _hasImage = true;
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint w, nint l) =>
        msg == WM_NCHITTEST ? HTTRANSPARENT : DefWindowProc(hwnd, msg, w, l);
}
