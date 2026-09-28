using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CursorForge;

/// <summary>Renders the README images (preset gallery, cursor states) with the real cursor renderer.</summary>
static class ReadmeImages
{
    static readonly Brush Bg = Frozen(Color.FromRgb(0x0D, 0x0F, 0x13));
    static readonly Brush Tile = Frozen(Color.FromRgb(0x1C, 0x20, 0x28));
    static readonly Brush TextBrush = Frozen(Color.FromRgb(0xEC, 0xEE, 0xF3));
    static readonly Brush Muted = Frozen(Color.FromRgb(0x8B, 0x92, 0xA3));
    static readonly Typeface Font = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    /// <summary>Cursors are drawn a bit larger than 1:1 so they read well in a README.</summary>
    const float Zoom = 1.5f;

    public static void Write(string dir)
    {
        Directory.CreateDirectory(dir);
        Save(Presets_(), Path.Combine(dir, "presets.png"));
        Save(States(), Path.Combine(dir, "states.png"));
    }

    static DrawingVisual Presets_()
    {
        const int cols = 5, tileW = 164, tileH = 150, gap = 14, pad = 24;
        var all = Presets.All;
        int rows = (all.Count + cols - 1) / cols;
        int w = pad * 2 + cols * tileW + (cols - 1) * gap, h = pad * 2 + rows * tileH + (rows - 1) * gap;

        var v = new DrawingVisual();
        using var dc = v.RenderOpen();
        dc.DrawRoundedRectangle(Bg, null, new Rect(0, 0, w, h), 18, 18);
        for (int i = 0; i < all.Count; i++)
        {
            double x = pad + i % cols * (tileW + gap), y = pad + i / cols * (tileH + gap);
            dc.DrawRoundedRectangle(Tile, null, new Rect(x, y, tileW, tileH), 14, 14);
            var bmp = ToBitmap(CursorRenderer.Render(Zoomed(all[i].Style)));
            double bw = Math.Min(bmp.PixelWidth, tileW - 20), bh = Math.Min(bmp.PixelHeight, tileH - 44);
            dc.DrawImage(bmp, new Rect(x + (tileW - bw) / 2, y + 12 + (tileH - 44 - bh) / 2, bw, bh));
            var label = Text(all[i].Name, 14, TextBrush);
            dc.DrawText(label, new Point(x + (tileW - label.Width) / 2, y + tileH - 30));
        }
        return v;
    }

    static DrawingVisual States()
    {
        string[] ids = ["classic", "neon", "sunburst", "violet", "spire"];
        var roles = Enum.GetValues<CursorRole>();
        const int cell = 64, labelW = 110, pad = 24, gap = 6, captionH = 34;
        int w = pad * 2 + labelW + roles.Length * (cell + gap) - gap, h = pad * 2 + ids.Length * (cell + gap) - gap + captionH;

        var v = new DrawingVisual();
        using var dc = v.RenderOpen();
        dc.DrawRoundedRectangle(Bg, null, new Rect(0, 0, w, h), 18, 18);
        for (int r = 0; r < ids.Length; r++)
        {
            var preset = Presets.Find(ids[r])!;
            double y = pad + r * (cell + gap);
            var name = Text(preset.Name, 14, TextBrush);
            dc.DrawText(name, new Point(pad, y + (cell - name.Height) / 2));
            for (int c = 0; c < roles.Length; c++)
            {
                double x = pad + labelW + c * (cell + gap);
                dc.DrawRoundedRectangle(Tile, null, new Rect(x, y, cell, cell), 10, 10);
                var style = preset.Style.Clone();
                style.Size = Math.Min(style.Size, 40);
                var bmp = ToBitmap(CursorRenderer.RenderRole(style, roles[c], frame: 3));
                double bw = Math.Min(bmp.PixelWidth, cell - 6), bh = Math.Min(bmp.PixelHeight, cell - 6);
                dc.DrawImage(bmp, new Rect(x + (cell - bw) / 2, y + (cell - bh) / 2, bw, bh));
            }
        }
        var caption = Text("Normal · Link · Text · Busy · Working · Unavailable · Precision · Move · Resize ↕ ↔ ⤡ ⤢ · Alternate · Help · Pin · Person",
            12.5, Muted);
        dc.DrawText(caption, new Point(pad + labelW, h - pad - caption.Height + 4));
        return v;
    }

    static CursorStyle Zoomed(CursorStyle s)
    {
        var z = s.Clone();
        z.Size = (int)MathF.Round(s.Size * Zoom);
        z.OutlineWidth *= Zoom;
        z.GlowRadius *= Zoom;
        z.HotspotDotSize *= Zoom;
        return z;
    }

    static BitmapSource ToBitmap(RenderedCursor rc)
    {
        var bmp = BitmapSource.Create(rc.Width, rc.Height, 96, 96, PixelFormats.Bgra32, null, rc.Pixels, rc.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    static FormattedText Text(string s, double size, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush, 1.0);

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static void Save(DrawingVisual v, string path)
    {
        var bounds = v.ContentBounds;
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
        Console.WriteLine($"Wrote {path} ({rtb.PixelWidth}x{rtb.PixelHeight})");
    }
}
