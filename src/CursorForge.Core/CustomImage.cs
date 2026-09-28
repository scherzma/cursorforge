namespace CursorForge;

/// <summary>A user-imported cursor image, stored as raw straight-alpha BGRA so the agent needs no image decoders.</summary>
public sealed class CustomImage(int width, int height, byte[] pixels)
{
    const uint Magic = 0x31494643; // "CFI1"

    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;

    static readonly Lock CacheLock = new();
    static (string Path, DateTime Stamp, CustomImage Image)? _cache;

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        using (var bw = new BinaryWriter(File.Create(tmp)))
        {
            bw.Write(Magic);
            bw.Write(Width);
            bw.Write(Height);
            bw.Write(Pixels);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static CustomImage? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var br = new BinaryReader(File.OpenRead(path));
            if (br.ReadUInt32() != Magic) return null;
            int w = br.ReadInt32(), h = br.ReadInt32();
            if (w is < 1 or > 1024 || h is < 1 or > 1024) return null;
            var px = br.ReadBytes(w * h * 4);
            return px.Length == w * h * 4 ? new CustomImage(w, h, px) : null;
        }
        catch
        {
            return null;
        }
    }

    public static CustomImage? LoadCached(string path)
    {
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); }
        catch { return null; }
        lock (CacheLock)
        {
            if (_cache is { } c && c.Path == path && c.Stamp == stamp) return c.Image;
            var img = TryLoad(path);
            _cache = img == null ? null : (path, stamp, img);
            return img;
        }
    }
}
