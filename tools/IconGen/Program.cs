// Generates assets/app.ico from the cursor renderer, so the app icon matches the cursors it makes.
using System.IO;
using CursorForge;

if (args is ["--sheet", var sheetPath]) { WriteSheet(sheetPath); return; }
if (args is ["--readme", var readmeDir])
{
    // WPF rendering needs an STA thread.
    var t = new Thread(() => ReadmeImages.Write(readmeDir));
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
    return;
}

string output = args.Length > 0 ? args[0] : "app.ico";
var style = new CursorStyle
{
    Shape = CursorShape.Pointer, Fill = "#FF8B6CFF", Outline = "#FFFFFFFF", OutlineWidth = 2f, Shadow = false,
};
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

var images = sizes.Select(px =>
{
    var s = style.Clone();
    s.OutlineWidth = Math.Max(1f, px / 20f);
    return CursorRenderer.RenderIcon(s, px);
}).ToList();

using var fs = File.Create(output);
using var bw = new BinaryWriter(fs);
bw.Write((ushort)0); // reserved
bw.Write((ushort)1); // type: icon
bw.Write((ushort)images.Count);

var blobs = images.Select(ToBmpBlob).ToList();
int offset = 6 + 16 * images.Count;
for (int i = 0; i < images.Count; i++)
{
    var img = images[i];
    bw.Write((byte)(img.Width >= 256 ? 0 : img.Width));
    bw.Write((byte)(img.Height >= 256 ? 0 : img.Height));
    bw.Write((byte)0); // palette
    bw.Write((byte)0); // reserved
    bw.Write((ushort)1); // planes
    bw.Write((ushort)32); // bpp
    bw.Write(blobs[i].Length);
    bw.Write(offset);
    offset += blobs[i].Length;
}
foreach (var blob in blobs) bw.Write(blob);
Console.WriteLine($"Wrote {output}");

// Debug aid: every preset on a light and a dark row, written as a 32-bit BMP.
static void WriteSheet(string path)
{
    const int cell = 110;
    // rows: a few presets, each drawn in every cursor state (light background), last row dark
    string[] ids = ["classic", "neon", "sunburst", "ring", "glass"];
    var roles = Enum.GetValues<CursorRole>();
    int w = cell * roles.Length, h = cell * (ids.Length + 1);
    var rows = ids.Select(id => Presets.Find(id)!.Style).Append(Presets.Find("midnight")!.Style).ToList();
    var px = new byte[w * h * 4];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            byte v = y < cell * ids.Length ? (byte)232 : (byte)12;
            int i = (y * w + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = v; px[i + 3] = 255;
        }
    for (int row = 0; row < rows.Count; row++)
    for (int p = 0; p < roles.Length; p++)
    {
        var rc = CursorRenderer.RenderRole(rows[row], roles[p], frame: 3);
        {
            int ox = p * cell + (cell - rc.Width) / 2, oy = row * cell + (cell - rc.Height) / 2;
            for (int y = 0; y < rc.Height; y++)
                for (int x = 0; x < rc.Width; x++)
                {
                    int s = (y * rc.Width + x) * 4, d = ((oy + y) * w + ox + x) * 4;
                    float a = rc.Pixels[s + 3] / 255f;
                    for (int c = 0; c < 3; c++) px[d + c] = (byte)(rc.Pixels[s + c] * a + px[d + c] * (1 - a));
                    if (x == rc.HotX && y == rc.HotY) { px[d] = 0; px[d + 1] = 0; px[d + 2] = 255; } // hotspot marker
                }
        }
    }
    using var bw = new BinaryWriter(File.Create(path));
    bw.Write((ushort)0x4D42); bw.Write(54 + px.Length); bw.Write(0); bw.Write(54);
    bw.Write(40); bw.Write(w); bw.Write(-h); bw.Write((ushort)1); bw.Write((ushort)32);
    bw.Write(0); bw.Write(px.Length); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
    bw.Write(px);
}

static byte[] ToBmpBlob(RenderedCursor img)
{
    int w = img.Width, h = img.Height;
    int maskStride = (w + 31) / 32 * 4;
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);
    bw.Write(40); bw.Write(w); bw.Write(h * 2); // XOR + AND height
    bw.Write((ushort)1); bw.Write((ushort)32);
    bw.Write(0); bw.Write(w * h * 4 + maskStride * h);
    bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
    for (int y = h - 1; y >= 0; y--) bw.Write(img.Pixels, y * w * 4, w * 4); // bottom-up
    bw.Write(new byte[maskStride * h]);
    return ms.ToArray();
}
