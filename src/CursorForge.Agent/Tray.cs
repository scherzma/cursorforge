using static CursorForge.Agent.Native;

namespace CursorForge.Agent;

internal sealed unsafe class Tray(nint hwnd)
{
    public const uint CallbackMessage = Ipc.WM_APP + 10;

    nint _icon;
    string _tip = "CursorForge";

    public bool Added { get; private set; }

    public void Update(RenderedCursor iconImage, string tip)
    {
        nint old = _icon;
        _icon = SystemCursors.CreateHandle(iconImage, isIcon: true);
        _tip = tip;
        if (Added) Added = Notify(NIM_MODIFY, NIF_ICON | NIF_TIP | NIF_MESSAGE) || Notify(NIM_ADD, NIF_ICON | NIF_TIP | NIF_MESSAGE);
        else Added = Notify(NIM_ADD, NIF_ICON | NIF_TIP | NIF_MESSAGE);
        if (old != 0) DestroyIcon(old);
    }

    /// <summary>Explorer restarted (or wasn't ready at logon): add the icon again.</summary>
    public void Readd()
    {
        Notify(NIM_DELETE, 0);
        Added = Notify(NIM_ADD, NIF_ICON | NIF_TIP | NIF_MESSAGE);
    }

    public void Remove()
    {
        if (Added) Notify(NIM_DELETE, 0);
        Added = false;
        if (_icon != 0) DestroyIcon(_icon);
        _icon = 0;
    }

    public void Balloon(string title, string text) => Notify(NIM_MODIFY, NIF_INFO, title, text);

    bool Notify(uint msg, uint flags, string? infoTitle = null, string? info = null)
    {
        var d = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        Copy(d.szTip, 128, _tip);
        if (info != null)
        {
            Copy(d.szInfo, 256, info);
            Copy(d.szInfoTitle, 64, infoTitle ?? "");
            d.dwInfoFlags = NIIF_INFO | NIIF_NOSOUND;
        }
        return Shell_NotifyIcon(msg, &d) != 0;
    }

    static void Copy(char* dst, int capacity, string s)
    {
        int n = Math.Min(s.Length, capacity - 1);
        for (int i = 0; i < n; i++) dst[i] = s[i];
        dst[n] = '\0';
    }
}
