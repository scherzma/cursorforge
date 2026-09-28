using System.Text;

namespace CursorForge;

/// <summary>
/// Writes .cur / .ani files. A file may hold several resolutions of the same cursor; when Windows loads it
/// through the cursor scheme it picks the resolution matching the pointer size and shows it without scaling.
/// </summary>
public static class CursorFile
{
    public static byte[] ToCur(RenderedCursor rc) => ToCur([rc]);

    public static byte[] ToCur(IReadOnlyList<RenderedCursor> sizes)
    {
        var dibs = sizes.Select(ToDib).ToList();
        var mono = sizes.Select(IsMonochrome).ToList();
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);
        bw.Write((ushort)2); // cursor
        bw.Write((ushort)sizes.Count);
        int offset = 6 + 16 * sizes.Count;
        for (int i = 0; i < sizes.Count; i++)
        {
            var rc = sizes[i];
            bw.Write((byte)(rc.Width >= 256 ? 0 : rc.Width));
            bw.Write((byte)(rc.Height >= 256 ? 0 : rc.Height));
            bw.Write((byte)(mono[i] ? 2 : 0)); // colour count
            bw.Write((byte)0);
            bw.Write((ushort)rc.HotX);
            bw.Write((ushort)rc.HotY);
            bw.Write(dibs[i].Length);
            bw.Write(offset);
            offset += dibs[i].Length;
        }
        foreach (var dib in dibs) bw.Write(dib);
        return ms.ToArray();
    }

    public static void WriteCur(string path, IReadOnlyList<RenderedCursor> sizes) => WriteAtomic(path, ToCur(sizes));

    /// <param name="frames">Animation frames, each with one or more resolutions.</param>
    public static void WriteAni(string path, IReadOnlyList<IReadOnlyList<RenderedCursor>> frames, int jiffiesPerFrame)
    {
        var curs = frames.Select(ToCur).ToList();

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(0); // patched below
        bw.Write(Encoding.ASCII.GetBytes("ACON"));

        bw.Write(Encoding.ASCII.GetBytes("anih"));
        bw.Write(36);
        bw.Write(36);               // cbSize
        bw.Write(frames.Count);     // nFrames
        bw.Write(frames.Count);     // nSteps
        bw.Write(0); bw.Write(0);   // cx, cy (taken from the frames)
        bw.Write(0); bw.Write(0);   // bit count, planes
        bw.Write(jiffiesPerFrame);  // display rate
        bw.Write(1);                // AF_ICON: frames are .cur data

        bw.Write(Encoding.ASCII.GetBytes("LIST"));
        bw.Write(4 + curs.Sum(c => 8 + c.Length + (c.Length & 1)));
        bw.Write(Encoding.ASCII.GetBytes("fram"));
        foreach (var c in curs)
        {
            bw.Write(Encoding.ASCII.GetBytes("icon"));
            bw.Write(c.Length);
            bw.Write(c);
            if ((c.Length & 1) != 0) bw.Write((byte)0);
        }

        bw.Flush();
        var bytes = ms.ToArray();
        BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
        WriteAtomic(path, bytes);
    }

    static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// An inverted cursor whose every pixel is transparent, inverting, black or white fits the classic 1-bpp
    /// AND/XOR format, which display hardware draws itself (see <see cref="CursorStyle.InvertIsMonochrome"/>).
    /// </summary>
    public static bool IsMonochrome(RenderedCursor rc)
    {
        if (rc.Mask == null) return false;
        for (int i = 0, n = rc.Width * rc.Height; i < n; i++)
        {
            int p = i * 4;
            bool black = rc.Pixels[p] <= 40 && rc.Pixels[p + 1] <= 40 && rc.Pixels[p + 2] <= 40;
            bool white = rc.Pixels[p] >= 215 && rc.Pixels[p + 1] >= 215 && rc.Pixels[p + 2] >= 215;
            if (!black && !white) return false;
        }
        return true;
    }

    /// <summary>1-bpp payload: header, 2-colour table, XOR bitmap then AND mask (both bottom-up).</summary>
    static byte[] ToMonochromeDib(RenderedCursor rc)
    {
        int w = rc.Width, h = rc.Height, stride = (w + 31) / 32 * 4;
        var xor = new byte[stride * h];
        var and = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            int row = (h - 1 - y) * stride;
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                byte bit = (byte)(0x80 >> (x & 7));
                if (rc.Pixels[i * 4] >= 128) xor[row + (x >> 3)] |= bit;    // white / invert
                if (rc.Mask![i] != 0) and[row + (x >> 3)] |= bit;          // keep (and possibly invert) the screen
            }
        }
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(w); bw.Write(h * 2);
        bw.Write((ushort)1); bw.Write((ushort)1);
        bw.Write(0); bw.Write(xor.Length + and.Length);
        bw.Write(0); bw.Write(0); bw.Write(2); bw.Write(0);
        bw.Write(0x00000000); // palette: black
        bw.Write(0x00FFFFFF); //          white
        bw.Write(xor);
        bw.Write(and);
        return ms.ToArray();
    }

    /// <summary>32-bit BMP-in-ICO payload: header with doubled height, bottom-up BGRA, empty AND mask.</summary>
    static byte[] ToDib(RenderedCursor rc)
    {
        if (IsMonochrome(rc)) return ToMonochromeDib(rc);
        int w = rc.Width, h = rc.Height, maskStride = (w + 31) / 32 * 4;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(w); bw.Write(h * 2);
        bw.Write((ushort)1); bw.Write((ushort)32);
        bw.Write(0); bw.Write(w * h * 4 + maskStride * h);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        for (int y = h - 1; y >= 0; y--) bw.Write(rc.Pixels, y * w * 4, w * 4);
        var and = new byte[maskStride * h];
        if (rc.Mask != null)
        {
            // 1 bit per pixel, MSB first, bottom-up rows. (With all-zero alpha, Windows uses this AND mask.)
            for (int y = 0; y < h; y++)
            {
                int row = (h - 1 - y) * maskStride;
                for (int x = 0; x < w; x++)
                    if (rc.Mask[y * w + x] != 0) and[row + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
        bw.Write(and);
        return ms.ToArray();
    }
}

/// <summary>The Windows "Mouse pointer size" setting, which fixes the pixel size of scheme cursors.</summary>
public static class WindowsPointer
{
    /// <summary>
    /// Scheme canvas needed for this style: the user's pointer size, raised (in steps of 8 px) when the cursor
    /// needs more room. Hardware cursors top out at 256 px.
    /// </summary>
    public static int CanvasFor(CursorStyle style, int userBaseSize)
    {
        int needed = CursorRenderer.Render(style).Width;
        needed = (needed + 7) / 8 * 8;
        return Math.Clamp(Math.Max(userBaseSize, needed), 32, CursorRenderer.MaxCanvas);
    }

    /// <summary>The user's own pointer size as saved in their settings (32 = default size).</summary>
    public static int BaseSize()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            if (key?.GetValue("CursorBaseSize") is int v) return Math.Clamp(v, 32, 256);
        }
        catch { }
        return 32;
    }

    /// <summary>Frame sizes to put in each cursor file: the pointer size plus common higher-DPI variants.</summary>
    public static int[] FrameSizes(int baseSize) =>
        new[] { baseSize, baseSize * 5 / 4, baseSize * 3 / 2, baseSize * 2 }
            .Select(s => Math.Min(s, CursorRenderer.MaxCanvas)).Distinct().ToArray();
}
