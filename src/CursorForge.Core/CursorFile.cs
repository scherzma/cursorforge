using System.Text;

namespace CursorForge;

/// <summary>Writes .cur / .ani files. Animated cursors can only be handed to Windows as .ani files.</summary>
public static class CursorFile
{
    public static byte[] ToCur(RenderedCursor rc)
    {
        byte[] dib = ToDib(rc);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);
        bw.Write((ushort)2); // cursor
        bw.Write((ushort)1);
        bw.Write((byte)(rc.Width >= 256 ? 0 : rc.Width));
        bw.Write((byte)(rc.Height >= 256 ? 0 : rc.Height));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)rc.HotX);
        bw.Write((ushort)rc.HotY);
        bw.Write(dib.Length);
        bw.Write(22);
        bw.Write(dib);
        return ms.ToArray();
    }

    public static void WriteAni(string path, IReadOnlyList<RenderedCursor> frames, int jiffiesPerFrame)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>32-bit BMP-in-ICO payload: header with doubled height, bottom-up BGRA, empty AND mask.</summary>
    static byte[] ToDib(RenderedCursor rc)
    {
        int w = rc.Width, h = rc.Height, maskStride = (w + 31) / 32 * 4;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(w); bw.Write(h * 2);
        bw.Write((ushort)1); bw.Write((ushort)32);
        bw.Write(0); bw.Write(w * h * 4 + maskStride * h);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        for (int y = h - 1; y >= 0; y--) bw.Write(rc.Pixels, y * w * 4, w * 4);
        bw.Write(new byte[maskStride * h]);
        return ms.ToArray();
    }
}
