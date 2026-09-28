using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CursorForge.Ui;

internal static partial class NativeUi
{
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindow(string className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    public static partial int PostMessage(nint hwnd, uint msg, nint w, nint l);

    [LibraryImport("user32.dll")]
    public static partial int ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    public static partial int SetForegroundWindow(nint hwnd);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar whose caption colour matches the window background.</summary>
    public static void ApplyDarkChrome(Window w)
    {
        nint hwnd = new WindowInteropHelper(w).Handle;
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);
        int caption = 0x00130F0D; // COLORREF of #0D0F13
        DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref caption, 4);
    }
}

internal static class AgentClient
{
    public static string AgentPath => Path.Combine(AppContext.BaseDirectory, Ipc.AgentExe);

    public static bool IsInstalled => File.Exists(AgentPath);

    public static bool IsRunning => NativeUi.FindWindow(Ipc.AgentWindowClass, null) != 0;

    public static void EnsureRunning()
    {
        if (!IsRunning) Start();
    }

    public static void Start()
    {
        if (!IsInstalled) return;
        try { Process.Start(new ProcessStartInfo(AgentPath) { UseShellExecute = false })?.Dispose(); }
        catch { }
    }

    /// <summary>Tells the agent to re-read config.json (starting it if needed).</summary>
    public static void Reload()
    {
        nint h = NativeUi.FindWindow(Ipc.AgentWindowClass, null);
        if (h != 0) NativeUi.PostMessage(h, Ipc.MsgReload, 0, 0);
        else Start();
    }

    public static void Stop()
    {
        nint h = NativeUi.FindWindow(Ipc.AgentWindowClass, null);
        if (h != 0) NativeUi.PostMessage(h, Ipc.MsgExit, 0, 0);
    }
}

internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "CursorForge";

    public static void Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on && AgentClient.IsInstalled) key.SetValue(ValueName, $"\"{AgentClient.AgentPath}\"");
            else if (!on) key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { }
    }
}

internal static class ImageImport
{
    /// <summary>Decodes png/jpg/bmp/gif/ico/cur into a CustomImage plus a hotspot (fraction of size).</summary>
    public static (CustomImage Image, float HotX, float HotY) Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        float hx = 0, hy = 0;

        // .cur is an .ico with type 2 and hotspots in the directory. Read the hotspot, then decode it as an .ico.
        if (bytes.Length > 22 && BitConverter.ToUInt16(bytes, 0) == 0 && BitConverter.ToUInt16(bytes, 2) == 2)
        {
            int count = BitConverter.ToUInt16(bytes, 4), best = 0, bestW = -1;
            for (int i = 0; i < count && 6 + i * 16 + 16 <= bytes.Length; i++)
            {
                int w = bytes[6 + i * 16] == 0 ? 256 : bytes[6 + i * 16];
                if (w > bestW) { bestW = w; best = i; }
            }
            int e = 6 + best * 16;
            int ew = bytes[e] == 0 ? 256 : bytes[e], eh = bytes[e + 1] == 0 ? 256 : bytes[e + 1];
            hx = BitConverter.ToUInt16(bytes, e + 4) / (float)ew;
            hy = BitConverter.ToUInt16(bytes, e + 6) / (float)eh;
            bytes = (byte[])bytes.Clone();
            bytes[2] = 1;
        }

        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
        int max = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (max > CursorRenderer.MaxCanvas)
        {
            double s = (double)CursorRenderer.MaxCanvas / max;
            frame = new TransformedBitmap(frame, new ScaleTransform(s, s));
        }
        var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int pw = conv.PixelWidth, ph = conv.PixelHeight;
        var px = new byte[pw * ph * 4];
        conv.CopyPixels(px, pw * 4, 0);
        return (new CustomImage(pw, ph, px), Math.Clamp(hx, 0, 1), Math.Clamp(hy, 0, 1));
    }
}
