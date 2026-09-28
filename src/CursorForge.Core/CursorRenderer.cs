using System.Globalization;
using System.Numerics;

namespace CursorForge;

/// <summary>A rendered cursor image: straight (non-premultiplied) BGRA, top-down rows.</summary>
public sealed class RenderedCursor
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int HotX { get; init; }
    public required int HotY { get; init; }
    public required byte[] Pixels { get; init; }
}

public readonly record struct Rgba(float R, float G, float B, float A)
{
    public static bool TryParse(string? s, out Rgba c)
    {
        c = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().TrimStart('#');
        if (s.Length is not (6 or 8)) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
        if (s.Length == 6) v |= 0xFF000000;
        c = new Rgba(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, (v >> 24) / 255f);
        return true;
    }

    public static Rgba Parse(string? s) => TryParse(s, out var c) ? c : new Rgba(1, 1, 1, 1);
}

/// <summary>
/// Renders cursors from signed distance fields. Every shape is an exact SDF, so edges get analytic
/// anti-aliasing at any size, and outline / glow / shadow fall out of the same distance value.
/// </summary>
public static class CursorRenderer
{
    public const int MaxCanvas = 256;

    /// <summary>Busy / working spinners: frame count and delay per frame in 1/60 s (jiffies).</summary>
    public const int SpinnerFrames = 20, SpinnerJiffies = 3;

    public static RenderedCursor Render(CursorStyle style) =>
        RenderCore(style, style.Size, style.Glow, style.Shadow);

    public static bool IsAnimated(CursorRole role) => role is CursorRole.Busy or CursorRole.Working;

    /// <summary>Renders the cursor for one Windows cursor state, in the style of the main pointer.</summary>
    public static RenderedCursor RenderRole(CursorStyle style, CursorRole role, int frame = 0) =>
        role == CursorRole.Pointer
            ? Render(style)
            : RenderCore(style, style.Size, style.Glow, style.Shadow, role, frame);

    /// <summary>
    /// Renders one frame for a Windows cursor scheme file: exactly canvas x canvas pixels. Windows shows the
    /// frame whose size matches its pointer size 1:1, so <paramref name="baseSize"/> (the pointer size) is
    /// where the style is drawn at its own pixel size; larger canvases (other monitor DPIs) scale it up.
    /// Anything that doesn't fit is shrunk until it does.
    /// </summary>
    public static RenderedCursor RenderFrame(CursorStyle style, CursorRole role, int frame, int baseSize, int canvas)
    {
        float k = canvas / (float)baseSize;
        var s = style.Clone();
        s.OutlineWidth *= k;
        s.GlowRadius *= k;
        int size = Math.Max(4, (int)MathF.Round(style.Size * k));
        RenderedCursor rc;
        while (true)
        {
            s.Size = size;
            rc = RenderRole(s, role, frame);
            if (rc.Width <= canvas || size <= 4) break;
            size = Math.Max(4, size - Math.Max(1, (rc.Width - canvas) / 2));
        }
        if (rc.Width == canvas && rc.Height == canvas) return rc;

        var px = new byte[canvas * canvas * 4];
        int cw = Math.Min(canvas, rc.Width), ch = Math.Min(canvas, rc.Height);
        for (int y = 0; y < ch; y++) Buffer.BlockCopy(rc.Pixels, y * rc.Width * 4, px, y * canvas * 4, cw * 4);
        return new RenderedCursor
        {
            Width = canvas, Height = canvas, Pixels = px,
            HotX = Math.Min(rc.HotX, canvas - 1), HotY = Math.Min(rc.HotY, canvas - 1),
        };
    }

    /// <summary>The style with its size capped to what fits the Windows pointer size (what actually shows on screen).</summary>
    public static CursorStyle Effective(CursorStyle style, int baseSize)
    {
        var s = style.Clone();
        s.Size = Math.Min(s.Size, MaxSizeFor(style, baseSize));
        return s;
    }

    /// <summary>Largest pointer size that fits a canvas of the given size without shrinking.</summary>
    public static int MaxSizeFor(CursorStyle style, int canvas)
    {
        var s = style.Clone();
        int lo = 8, hi = 200;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            s.Size = mid;
            if (Render(s).Width <= canvas) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Renders a flat (no glow / shadow) version that fits and is centered in a px*px square.</summary>
    public static RenderedCursor RenderIcon(CursorStyle style, int px)
    {
        px = Math.Clamp(px, 8, MaxCanvas);
        int size = px;
        RenderedCursor rc;
        (int x0, int y0, int x1, int y1) bb;
        while (true)
        {
            rc = RenderCore(style, size, glow: false, shadow: false);
            bb = Bounds(rc);
            int ext = Math.Max(bb.x1 - bb.x0, bb.y1 - bb.y0);
            if (ext <= px || size <= 4) break;
            size = Math.Max(4, size - Math.Max(1, (ext - px) / 2));
        }

        var outPx = new byte[px * px * 4];
        int bw = bb.x1 - bb.x0, bh = bb.y1 - bb.y0;
        int dx = (px - bw) / 2 - bb.x0, dy = (px - bh) / 2 - bb.y0;
        for (int y = bb.y0; y < bb.y1; y++)
        {
            int ty = y + dy;
            if ((uint)ty >= (uint)px) continue;
            for (int x = bb.x0; x < bb.x1; x++)
            {
                int tx = x + dx;
                if ((uint)tx >= (uint)px) continue;
                Buffer.BlockCopy(rc.Pixels, (y * rc.Width + x) * 4, outPx, (ty * px + tx) * 4, 4);
            }
        }
        return new RenderedCursor { Width = px, Height = px, HotX = 0, HotY = 0, Pixels = outPx };
    }

    static (int, int, int, int) Bounds(RenderedCursor rc)
    {
        int x0 = rc.Width, y0 = rc.Height, x1 = 0, y1 = 0;
        for (int y = 0; y < rc.Height; y++)
            for (int x = 0; x < rc.Width; x++)
                if (rc.Pixels[(y * rc.Width + x) * 4 + 3] > 8)
                {
                    if (x < x0) x0 = x;
                    if (y < y0) y0 = y;
                    if (x + 1 > x1) x1 = x + 1;
                    if (y + 1 > y1) y1 = y + 1;
                }
        return x1 <= x0 ? (0, 0, rc.Width, rc.Height) : (x0, y0, x1, y1);
    }

    static RenderedCursor RenderCore(CursorStyle s, int size, bool glow, bool shadow,
        CursorRole role = CursorRole.Pointer, int frame = 0)
    {
        if (s.Shape == CursorShape.Custom && role == CursorRole.Pointer)
        {
            var custom = RenderCustom(s, size);
            if (custom != null) return custom;
            s = s.Clone();
            s.Shape = CursorShape.Arrow; // image missing -> fall back to an arrow in the chosen colours
        }

        size = Math.Clamp(size, 4, 200);
        var g = Geometry.ForRole(s.Shape, role, size, frame);
        var fill = Rgba.Parse(s.Fill);
        var outline = Rgba.Parse(s.Outline);
        var glowC = Rgba.Parse(s.GlowColor);
        float ow = Math.Clamp(s.OutlineWidth, 0, 12);
        float gr = glow ? Math.Clamp(s.GlowRadius, 1, 40) : 0;
        float gs = Math.Clamp(s.GlowStrength, 0, 1);
        float opacity = Math.Clamp(s.Opacity, 0, 1);
        float sx = shadow ? MathF.Max(1f, size / 30f) : 0;
        float sy = shadow ? MathF.Max(1.5f, size / 22f) : 0;
        float sb = shadow ? MathF.Max(1.5f, size / 18f) : 0;

        float pad = MathF.Ceiling(ow + 1.5f + MathF.Max(glow ? gr * 1.6f : 0, shadow ? MathF.Max(sx, sy) + sb : 0));
        float half = g.CenterHotspot ? 0.5f : 0f; // put the centre of round shapes on a pixel centre
        // Pointers have a straight vertical left edge: land its outer (outline) edge exactly on a pixel
        // boundary so it stays one crisp column instead of being smeared across two.
        float snap = g.CenterHotspot ? 0f : (1f - (pad - ow) % 1f) % 1f;
        int w = (int)MathF.Ceiling(g.MaxX - g.MinX + 2 * pad + 2 * half + snap);
        int h = (int)MathF.Ceiling(g.MaxY - g.MinY + 2 * pad + 2 * half);
        int dim = Math.Clamp(Math.Max(w, h), 1, MaxCanvas);
        float ox = pad - g.MinX + half + snap, oy = pad - g.MinY + half;

        var px = new byte[dim * dim * 4];
        for (int y = 0; y < dim; y++)
        {
            float py = y + 0.5f - oy;
            for (int x = 0; x < dim; x++)
            {
                float pxx = x + 0.5f - ox;
                float d = g.Sdf(pxx, py);
                var acc = new Acc();

                if (shadow)
                {
                    float ds = g.Sdf(pxx - sx, py - sy) - ow;
                    acc.Over(new Rgba(0, 0, 0, 1), 0.42f * (1 - SmoothStep(-sb, sb, ds)));
                }

                if (glow)
                {
                    float dg = d - ow;
                    float t = Sat(dg / (gr * 1.5f));
                    acc.Over(glowC, gs * (1 - t) * (1 - t));
                }

                float cf = Sat(0.5f - d);
                float co = Sat(0.5f - (d - ow));
                acc.Over(outline, co - cf);
                acc.Over(fill, cf);

                if (g.Accent != null)
                    acc.Over(outline.A > 0 ? outline : fill, Sat(0.5f - g.Accent(pxx, py)));

                float a = acc.A * opacity;
                if (a <= 0.002f) continue;
                int i = (y * dim + x) * 4;
                float inv = opacity / a; // un-premultiply (acc is premultiplied, scaled by opacity)
                px[i + 0] = ToByte(acc.B * inv);
                px[i + 1] = ToByte(acc.G * inv);
                px[i + 2] = ToByte(acc.R * inv);
                px[i + 3] = ToByte(a);
            }
        }

        int hx = Math.Clamp((int)MathF.Floor(g.HotX + ox), 0, dim - 1);
        int hy = Math.Clamp((int)MathF.Floor(g.HotY + oy), 0, dim - 1);
        return new RenderedCursor { Width = dim, Height = dim, HotX = hx, HotY = hy, Pixels = px };
    }

    static RenderedCursor? RenderCustom(CursorStyle s, int size)
    {
        var img = CustomImage.LoadCached(ConfigStore.CustomImagePath);
        if (img == null) return null;

        float scale = Math.Clamp(size, 4, MaxCanvas) / (float)Math.Max(img.Width, img.Height);
        int nw = Math.Clamp((int)MathF.Round(img.Width * scale), 1, MaxCanvas);
        int nh = Math.Clamp((int)MathF.Round(img.Height * scale), 1, MaxCanvas);
        int dim = Math.Max(nw, nh);
        float opacity = Math.Clamp(s.Opacity, 0, 1);
        var src = img.Pixels;
        var px = new byte[dim * dim * 4];

        for (int y = 0; y < nh; y++)
        {
            float sy = (y + 0.5f) / nh * img.Height - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(sy), 0, img.Height - 1), y1 = Math.Min(y0 + 1, img.Height - 1);
            float fy = Sat(sy - y0);
            for (int x = 0; x < nw; x++)
            {
                float sxf = (x + 0.5f) / nw * img.Width - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(sxf), 0, img.Width - 1), x1 = Math.Min(x0 + 1, img.Width - 1);
                float fx = Sat(sxf - x0);
                // Bilinear in premultiplied space so transparent pixels don't bleed dark fringes.
                Vector4 c = Sample(src, img.Width, x0, y0) * (1 - fx) * (1 - fy)
                          + Sample(src, img.Width, x1, y0) * fx * (1 - fy)
                          + Sample(src, img.Width, x0, y1) * (1 - fx) * fy
                          + Sample(src, img.Width, x1, y1) * fx * fy;
                if (c.W <= 0.002f) continue;
                int i = (y * dim + x) * 4;
                px[i + 0] = ToByte(c.X / c.W);
                px[i + 1] = ToByte(c.Y / c.W);
                px[i + 2] = ToByte(c.Z / c.W);
                px[i + 3] = ToByte(c.W * opacity);
            }
        }

        int hx = Math.Clamp((int)MathF.Round(s.CustomHotX * nw), 0, nw - 1);
        int hy = Math.Clamp((int)MathF.Round(s.CustomHotY * nh), 0, nh - 1);
        return new RenderedCursor { Width = dim, Height = dim, HotX = hx, HotY = hy, Pixels = px };
    }

    static Vector4 Sample(byte[] p, int w, int x, int y)
    {
        int i = (y * w + x) * 4;
        float a = p[i + 3] / 255f;
        return new Vector4(p[i] / 255f * a, p[i + 1] / 255f * a, p[i + 2] / 255f * a, a);
    }

    struct Acc
    {
        public float R, G, B, A;

        public void Over(in Rgba c, float coverage)
        {
            float a = c.A * coverage;
            if (a <= 0) return;
            float k = 1 - a;
            R = c.R * a + R * k;
            G = c.G * a + G * k;
            B = c.B * a + B * k;
            A = a + A * k;
        }
    }

    static float Sat(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

    static float SmoothStep(float e0, float e1, float x)
    {
        float t = Sat((x - e0) / (e1 - e0));
        return t * t * (3 - 2 * t);
    }

    static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);

    sealed class Geometry
    {
        public float MinX, MinY, MaxX, MaxY, HotX, HotY;
        public bool CenterHotspot;
        public Func<float, float, float> Sdf = null!;
        public Func<float, float, float>? Accent;

        public static Geometry ForRole(CursorShape shape, CursorRole role, float size, int frame)
        {
            if (shape == CursorShape.Custom) shape = CursorShape.Arrow; // images can't be re-shaped per state
            float spin = frame * MathF.Tau / SpinnerFrames;
            switch (role)
            {
                case CursorRole.Pointer: return Create(shape, size);
                case CursorRole.Link: return Hand(size);
                case CursorRole.Text: return IBeam(size);
                case CursorRole.Busy:
                {
                    float r = size * 0.4f, t = MathF.Max(2f, size * 0.12f);
                    return Centered(r, (x, y) => Arc(x, y, r - t / 2, t, spin, MathF.Tau * 0.7f));
                }
                case CursorRole.Working:
                    return WithBadge(Create(shape, size), size, (x, y, br) =>
                    {
                        float t = MathF.Max(1.5f, br * 0.5f);
                        return Arc(x, y, br - t / 2, t, spin, MathF.Tau * 0.7f);
                    });
                case CursorRole.Unavailable: return NoSign(size * 0.75f);
                case CursorRole.Precision: return Create(CursorShape.Cross, size * 0.8f);
                case CursorRole.Move: return MoveArrows(size * 0.9f);
                case CursorRole.SizeWE: return DoubleArrow(size * 0.9f, 0);
                case CursorRole.SizeNS: return DoubleArrow(size * 0.9f, MathF.PI / 2);
                case CursorRole.SizeNWSE: return DoubleArrow(size * 0.95f, MathF.PI / 4);
                case CursorRole.SizeNESW: return DoubleArrow(size * 0.95f, -MathF.PI / 4);
                case CursorRole.Up: return UpArrow(size * 0.85f);
                default: // Help, Pin, Person: pointer + an info badge
                    return WithBadge(Create(shape, size), size, (x, y, br) =>
                    {
                        float l = Len(x, y), t = MathF.Max(1.5f, br * 0.36f);
                        return MathF.Min(MathF.Abs(l - (br - t / 2)) - t / 2, l - br * 0.3f);
                    });
            }
        }

        public static Geometry Create(CursorShape shape, float size)
        {
            float r = size / 2f;
            switch (shape)
            {
                case CursorShape.Pointer:
                    return Polygon([0, 0, 0, 17.5f, 4.9f, 13.2f, 12.6f, 12.6f], 17.5f, size, 0.9f);
                case CursorShape.Arrowhead:
                    return Polygon([0, 0, 15, 6, 7, 7, 6, 15], 15f, size, 0.6f);
                case CursorShape.Ring:
                {
                    float t = MathF.Max(2f, size * 0.13f), mid = r - t / 2;
                    return Centered(r, (x, y) => MathF.Abs(Len(x, y) - mid) - t / 2);
                }
                case CursorShape.Dot:
                    return Centered(r, (x, y) => Len(x, y) - r);
                case CursorShape.Target:
                {
                    float t = MathF.Max(2f, size * 0.11f), mid = r - t / 2, dot = MathF.Max(1.5f, size * 0.1f);
                    return Centered(r, (x, y) =>
                    {
                        float l = Len(x, y);
                        return MathF.Min(MathF.Abs(l - mid) - t / 2, l - dot);
                    });
                }
                case CursorShape.Cross:
                {
                    float t = MathF.Max(2f, size * 0.12f), gap = size * 0.16f, dot = t * 0.55f;
                    return Centered(r, (x, y) =>
                    {
                        float l = Len(x, y);
                        float arms = MathF.Min(Box(x, y, r, t / 2), Box(x, y, t / 2, r));
                        return MathF.Min(MathF.Max(arms, gap - l), l - dot);
                    });
                }
                case CursorShape.Diamond:
                    return Centered(r, (x, y) => (MathF.Abs(x) + MathF.Abs(y) - r) * 0.70710678f);
                case CursorShape.Halo:
                {
                    var g = Centered(r, (x, y) => Len(x, y) - r);
                    float dot = MathF.Max(1.5f, size * 0.07f);
                    g.Accent = (x, y) => Len(x, y) - dot;
                    return g;
                }
                default: // Arrow
                    return Polygon([0, 0, 0, 16.5f, 4.1f, 12.7f, 6.9f, 19.2f, 9.6f, 18.1f, 6.9f, 11.9f, 12.2f, 11.9f], 19.2f, size, 0.3f);
            }
        }

        static Geometry Centered(float r, Func<float, float, float> sdf) => new()
        {
            MinX = -r, MinY = -r, MaxX = r, MaxY = r, HotX = 0, HotY = 0, CenterHotspot = true, Sdf = sdf,
        };

        static Geometry Polygon(float[] pts, float gridHeight, float size, float round)
        {
            float u = size / gridHeight, rr = round * u;
            var v = new Vector2[pts.Length / 2];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = new Vector2(pts[2 * i] * u, pts[2 * i + 1] * u);
                minX = MathF.Min(minX, v[i].X); maxX = MathF.Max(maxX, v[i].X);
                minY = MathF.Min(minY, v[i].Y); maxY = MathF.Max(maxY, v[i].Y);
            }
            return new Geometry
            {
                MinX = minX - rr, MinY = minY - rr, MaxX = maxX + rr, MaxY = maxY + rr,
                HotX = 0, HotY = 0,
                Sdf = (x, y) => SdPolygon(v, new Vector2(x, y)) - rr,
            };
        }

        // ---- state glyphs ----

        /// <summary>Pointing hand, hotspot at the fingertip.</summary>
        static Geometry Hand(float size)
        {
            float u = size * 1.05f;
            return new Geometry
            {
                MinX = -0.27f * u, MinY = 0, MaxX = 0.5f * u, MaxY = 0.95f * u, HotX = 0, HotY = 0.01f * u,
                Sdf = (x, y) =>
                {
                    x /= u; y /= u;
                    float d = Capsule(x, y, 0, 0.09f, 0, 0.55f, 0.085f);                     // index finger
                    d = MathF.Min(d, Capsule(x, y, 0.155f, 0.45f, 0.155f, 0.6f, 0.072f));     // folded fingers
                    d = MathF.Min(d, Capsule(x, y, 0.29f, 0.48f, 0.29f, 0.6f, 0.07f));
                    d = MathF.Min(d, Capsule(x, y, 0.415f, 0.52f, 0.415f, 0.62f, 0.065f));
                    d = MathF.Min(d, RoundBox(x - 0.17f, y - 0.73f, 0.25f, 0.21f, 0.12f));    // palm
                    d = MathF.Min(d, Capsule(x, y, -0.07f, 0.74f, -0.2f, 0.58f, 0.065f));     // thumb
                    return d * u;
                },
            };
        }

        static Geometry IBeam(float size)
        {
            float h = size * 0.85f, t = MathF.Max(1.5f, size * 0.07f), serif = size * 0.15f;
            return Centered(h / 2, (x, y) => MathF.Min(Box(x, y, t / 2, h / 2),
                MathF.Min(Box(x, y - (h / 2 - t / 2), serif, t / 2), Box(x, y + (h / 2 - t / 2), serif, t / 2))));
        }

        static Geometry NoSign(float d)
        {
            float r = d / 2, t = MathF.Max(2f, d * 0.15f);
            return Centered(r, (x, y) =>
            {
                float ring = MathF.Abs(Len(x, y) - (r - t / 2)) - t / 2;
                float along = (x + y) * 0.70710678f, across = (y - x) * 0.70710678f; // top-left to bottom-right bar
                return MathF.Min(ring, Box(along, across, r - t / 2, t / 2));
            });
        }

        static Geometry DoubleArrow(float len, float angle)
        {
            float r = len / 2, hw = len * 0.2f, hh = len * 0.28f, sw = MathF.Max(1f, len * 0.065f);
            return PolyCentered([
                (r, 0), (r - hh, hw), (r - hh, sw), (-r + hh, sw), (-r + hh, hw),
                (-r, 0), (-r + hh, -hw), (-r + hh, -sw), (r - hh, -sw), (r - hh, -hw),
            ], angle, len * 0.02f);
        }

        static Geometry MoveArrows(float len)
        {
            float r = len / 2, hw = len * 0.15f, hh = len * 0.2f, sw = MathF.Max(1f, len * 0.05f);
            var pts = new List<(float, float)>();
            for (int k = 0; k < 4; k++)
            {
                // one arm pointing right, rotated by k * 90 degrees
                (float X, float Y)[] arm = [(sw, -sw), (r - hh, -sw), (r - hh, -hw), (r, 0), (r - hh, hw), (r - hh, sw)];
                float c = MathF.Round(MathF.Cos(k * MathF.PI / 2)), s = MathF.Round(MathF.Sin(k * MathF.PI / 2));
                foreach (var (px, py) in arm) pts.Add((px * c - py * s, px * s + py * c));
            }
            return PolyCentered([.. pts], 0, len * 0.015f);
        }

        static Geometry UpArrow(float len)
        {
            float r = len / 2, hw = len * 0.28f, hh = len * 0.36f, sw = MathF.Max(1f, len * 0.08f);
            var g = PolyCentered([(0, -r), (hw, -r + hh), (sw, -r + hh), (sw, r), (-sw, r), (-sw, -r + hh), (-hw, -r + hh)],
                0, len * 0.02f);
            g.HotY = -r;
            g.CenterHotspot = false;
            return g;
        }

        /// <summary>The main shape with a small badge (spinner / info) at its lower right.</summary>
        static Geometry WithBadge(Geometry main, float size, Func<float, float, float, float> badge)
        {
            float br = MathF.Max(3f, size * 0.2f);
            float cx = main.CenterHotspot ? main.MaxX * 0.8f : main.MaxX * 0.95f;
            float cy = main.CenterHotspot ? main.MaxY * 0.8f : main.MinY + (main.MaxY - main.MinY) * 0.8f;
            var inner = main.Sdf;
            return new Geometry
            {
                MinX = MathF.Min(main.MinX, cx - br), MinY = MathF.Min(main.MinY, cy - br),
                MaxX = MathF.Max(main.MaxX, cx + br), MaxY = MathF.Max(main.MaxY, cy + br),
                HotX = main.HotX, HotY = main.HotY, CenterHotspot = main.CenterHotspot,
                Sdf = (x, y) => MathF.Min(inner(x, y), badge(x - cx, y - cy, br)),
                Accent = main.Accent,
            };
        }

        static Geometry PolyCentered((float X, float Y)[] pts, float angle, float round)
        {
            float c = MathF.Cos(angle), s = MathF.Sin(angle), ext = 0;
            var v = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                v[i] = new Vector2(pts[i].X * c - pts[i].Y * s, pts[i].X * s + pts[i].Y * c);
                ext = MathF.Max(ext, MathF.Max(MathF.Abs(v[i].X), MathF.Abs(v[i].Y)));
            }
            ext += round;
            return new Geometry
            {
                MinX = -ext, MinY = -ext, MaxX = ext, MaxY = ext, CenterHotspot = true,
                Sdf = (x, y) => SdPolygon(v, new Vector2(x, y)) - round,
            };
        }

        /// <summary>Arc of a ring with round caps, starting at angle a0 and sweeping span radians.</summary>
        static float Arc(float x, float y, float rMid, float t, float a0, float span)
        {
            float rel = (MathF.Atan2(y, x) - a0) % MathF.Tau;
            if (rel < 0) rel += MathF.Tau;
            if (rel <= span) return MathF.Abs(Len(x, y) - rMid) - t / 2;
            float a1 = a0 + span;
            float d1 = Len(x - rMid * MathF.Cos(a0), y - rMid * MathF.Sin(a0));
            float d2 = Len(x - rMid * MathF.Cos(a1), y - rMid * MathF.Sin(a1));
            return MathF.Min(d1, d2) - t / 2;
        }

        static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
        {
            float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
            float h = Math.Clamp((pax * bax + pay * bay) / (bax * bax + bay * bay), 0f, 1f);
            return Len(pax - bax * h, pay - bay * h) - r;
        }

        static float RoundBox(float x, float y, float hx, float hy, float r) => Box(x, y, hx - r, hy - r) - r;

        static float Len(float x, float y) => MathF.Sqrt(x * x + y * y);

        static float Box(float x, float y, float bx, float by)
        {
            float qx = MathF.Abs(x) - bx, qy = MathF.Abs(y) - by;
            return Len(MathF.Max(qx, 0), MathF.Max(qy, 0)) + MathF.Min(MathF.Max(qx, qy), 0);
        }

        // Exact signed distance to a simple polygon (Inigo Quilez).
        static float SdPolygon(Vector2[] v, Vector2 p)
        {
            float d = Vector2.DistanceSquared(p, v[0]);
            float s = 1;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                Vector2 b = w - e * Math.Clamp(Vector2.Dot(w, e) / Vector2.Dot(e, e), 0f, 1f);
                d = MathF.Min(d, Vector2.Dot(b, b));
                bool c1 = p.Y >= v[i].Y, c2 = p.Y < v[j].Y, c3 = e.X * w.Y > e.Y * w.X;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * MathF.Sqrt(d);
        }
    }
}
