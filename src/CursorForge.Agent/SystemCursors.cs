using static CursorForge.Agent.Native;

namespace CursorForge.Agent;

/// <summary>
/// Replaces the Windows system cursors in place. The OS keeps drawing them as its normal hardware
/// cursor, so this path adds no latency and costs nothing after it's applied.
/// </summary>
internal static unsafe class SystemCursors
{
    const uint IMAGE_CURSOR = 2, LR_LOADFROMFILE = 0x10;

    public static void Apply(AppConfig cfg, RenderedCursor pointer)
    {
        var rendered = new Dictionary<CursorRole, RenderedCursor>();
        var animations = new Dictionary<CursorRole, string?>();

        foreach (var (role, id) in CursorRoles.Selected(cfg))
        {
            nint h;
            if (role == CursorRole.Pointer || cfg.StateCursors == StateCursorMode.Same)
            {
                h = CreateHandle(pointer, isIcon: false);
            }
            else if (CursorRenderer.IsAnimated(role))
            {
                if (!animations.TryGetValue(role, out var path)) animations[role] = path = WriteAnimation(cfg.Style, role);
                h = path == null ? 0 : LoadImage(0, path, IMAGE_CURSOR, 0, 0, LR_LOADFROMFILE);
            }
            else
            {
                if (!rendered.TryGetValue(role, out var rc)) rendered[role] = rc = CursorRenderer.RenderRole(cfg.Style, role);
                h = CreateHandle(rc, isIcon: false);
            }
            // SetSystemCursor takes ownership of the handle on success.
            if (h != 0 && SetSystemCursor(h, id) == 0) DestroyCursor(h);
        }
    }

    /// <summary>Spinners are real animated cursors; Windows only accepts those as .ani files.</summary>
    static string? WriteAnimation(CursorStyle style, CursorRole role)
    {
        try
        {
            var frames = Enumerable.Range(0, CursorRenderer.SpinnerFrames)
                .Select(f => (IReadOnlyList<RenderedCursor>)[CursorRenderer.RenderRole(style, role, f)]).ToList();
            string path = Path.Combine(ConfigStore.Dir, "cache", role.ToString().ToLowerInvariant() + ".ani");
            CursorFile.WriteAni(path, frames, CursorRenderer.SpinnerJiffies);
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reloads the user's cursor scheme from the registry (undoes every SetSystemCursor).</summary>
    public static void Restore() => SystemParametersInfo(SPI_SETCURSORS, 0, 0, 0);

    /// <summary>The shared handles of the standard cursors; an app showing anything else uses its own cursor.</summary>
    public static nint[] KnownHandles()
    {
        var list = new List<nint>(CursorRoles.All.Length);
        foreach (var (_, id) in CursorRoles.All)
        {
            nint h = LoadCursor(0, (nint)id);
            if (h != 0) list.Add(h);
        }
        return [.. list];
    }

    public static nint CreateHandle(RenderedCursor rc, bool isIcon)
    {
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = rc.Width,
            biHeight = -rc.Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        nint screen = GetDC(0);
        nint color = CreateDIBSection(screen, &bi, 0, &bits, 0, 0);
        ReleaseDC(0, screen);
        if (color == 0) return 0;
        fixed (byte* src = rc.Pixels)
            Buffer.MemoryCopy(src, bits, rc.Pixels.Length, rc.Pixels.Length);

        // All-zero AND mask: the alpha channel of the colour bitmap decides transparency.
        // AND mask: all zero (alpha decides), or the real mask of an inverted cursor (rows WORD-aligned, top-down).
        int stride = ((rc.Width + 15) / 16) * 2;
        var maskBits = new byte[stride * rc.Height];
        if (rc.Mask != null)
            for (int y = 0; y < rc.Height; y++)
                for (int x = 0; x < rc.Width; x++)
                    if (rc.Mask[y * rc.Width + x] != 0) maskBits[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        nint mask;
        fixed (byte* m = maskBits) mask = CreateBitmap(rc.Width, rc.Height, 1, 1, m);

        var ii = new ICONINFO
        {
            fIcon = isIcon ? 1 : 0,
            xHotspot = (uint)rc.HotX,
            yHotspot = (uint)rc.HotY,
            hbmMask = mask,
            hbmColor = color,
        };
        nint handle = CreateIconIndirect(&ii);
        DeleteObject(color);
        DeleteObject(mask);
        return handle;
    }
}
