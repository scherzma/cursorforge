using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using static CursorForge.Agent.Native;

namespace CursorForge.Agent;

/// <summary>
/// Applies cursors the way installed cursor themes do: multi-resolution .cur/.ani files referenced from
/// HKCU\Control Panel\Cursors, then a scheme reload. Windows picks the frame that matches the pointer size
/// and shows it 1:1 on the hardware cursor, so it stays pixel-sharp at any Windows pointer size. Cursors
/// created at runtime (SetSystemCursor) are instead stretched by the pointer-size factor and end up blurry.
///
/// The user's own values are backed up first and put back by <see cref="Restore"/>.
/// </summary>
internal static class SchemeCursors
{
    const string KeyPath = @"Control Panel\Cursors";
    const string RenderVersion = "6"; // bump when rendering changes, to regenerate cached files
    const uint SPI_SETCURSORBASESIZE = 0x2029; // undocumented; what Settings uses for "pointer size"

    static readonly (CursorRole Role, string Value)[] Values =
    [
        (CursorRole.Pointer, "Arrow"), (CursorRole.Link, "Hand"), (CursorRole.Text, "IBeam"),
        (CursorRole.Busy, "Wait"), (CursorRole.Working, "AppStarting"), (CursorRole.Unavailable, "No"),
        (CursorRole.Precision, "Crosshair"), (CursorRole.Move, "SizeAll"), (CursorRole.SizeNS, "SizeNS"),
        (CursorRole.SizeWE, "SizeWE"), (CursorRole.SizeNWSE, "SizeNWSE"), (CursorRole.SizeNESW, "SizeNESW"),
        (CursorRole.Up, "UpArrow"), (CursorRole.Help, "Help"), (CursorRole.Pin, "Pin"), (CursorRole.Person, "Person"),
    ];

    static string CursorsDir => Path.Combine(ConfigStore.Dir, "cursors");

    /// <summary>Folder of the currently applied cursor files (null when not applied).</summary>
    public static string? CurrentDir { get; private set; }

    public static string FlashPath(string dir, CursorRole role, bool right) =>
        FilePath(Path.Combine(dir, right ? "flash-right" : "flash-left"), role);
    static string BackupPath => Path.Combine(ConfigStore.Dir, "windows-cursors-backup.txt");

    public static void Apply(AppConfig cfg)
    {
        // Big cursors need a bigger canvas than the user's pointer size: raise it for this session only
        // (no SPIF_UPDATEINIFILE, so the saved setting is untouched and comes back at logon / on Restore).
        int baseSize = WindowsPointer.CanvasFor(cfg.Style, WindowsPointer.BaseSize());
        var selected = CursorRoles.Selected(cfg).Select(r => r.Role).ToHashSet();
        string dir = Path.Combine(CursorsDir, Fingerprint(cfg, baseSize));
        if (!File.Exists(Path.Combine(dir, "complete")))
        {
            Generate(cfg, baseSize, dir);
            // Rendering all frames churns through a few MB; hand it back to Windows instead of keeping it idle.
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }

        var backup = UpdateBackup();
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true))
        {
            foreach (var (role, value) in Values)
            {
                if (selected.Contains(role)) key.SetValue(value, FilePath(dir, role), RegistryValueKind.ExpandString);
                else RestoreValue(key, value, backup);
            }
        }
        SystemParametersInfo(SPI_SETCURSORBASESIZE, 0, baseSize, 0);
        SystemParametersInfo(SPI_SETCURSORS, 0, 0, 0);
        CurrentDir = dir;
        DeleteStale(dir);
    }

    /// <summary>Puts the user's previous cursor scheme back.</summary>
    public static void Restore()
    {
        CurrentDir = null;
        try
        {
            var backup = ReadBackup();
            if (backup.Count > 0)
            {
                using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
                foreach (var (_, value) in Values)
                {
                    // Only undo values that still point at our files; anything else was changed by the user since.
                    if (IsOurs(key.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string))
                        RestoreValue(key, value, backup);
                }
                File.Delete(BackupPath);
            }
        }
        catch { }
        SystemParametersInfo(SPI_SETCURSORBASESIZE, 0, WindowsPointer.BaseSize(), 0);
        SystemParametersInfo(SPI_SETCURSORS, 0, 0, 0);
    }

    static void Generate(AppConfig cfg, int baseSize, string dir)
    {
        Directory.CreateDirectory(dir);
        int[] sizes = WindowsPointer.FrameSizes(baseSize);
        var style = cfg.Style;
        bool same = cfg.StateCursors == StateCursorMode.Same;

        foreach (var (role, _) in Values)
        {
            var glyph = same ? CursorRole.Pointer : role;
            if (!same && CursorRenderer.IsAnimated(role))
            {
                var frames = Enumerable.Range(0, CursorRenderer.SpinnerFrames)
                    .Select(f => (IReadOnlyList<RenderedCursor>)sizes.Select(c => CursorRenderer.RenderFrame(style, glyph, f, baseSize, c)).ToList())
                    .ToList();
                CursorFile.WriteAni(FilePath(dir, role), frames, CursorRenderer.SpinnerJiffies);
            }
            else
            {
                CursorFile.WriteCur(FilePath(dir, role), sizes.Select(c => CursorRenderer.RenderFrame(style, glyph, 0, baseSize, c)).ToList());
            }
        }
        if (cfg.ClickFlash.Enabled)
        {
            foreach (bool right in new[] { false, true })
            {
                var flashStyle = cfg.ClickFlash.Apply(style, right);
                foreach (var (role, _) in Values)
                {
                    if (CursorRenderer.IsAnimated(role)) continue; // busy cursors keep spinning, never flashed
                    var glyph = same ? CursorRole.Pointer : role;
                    CursorFile.WriteCur(FlashPath(dir, role, right),
                        sizes.Select(c => CursorRenderer.RenderFrame(flashStyle, glyph, 0, baseSize, c)).ToList());
                }
            }
        }
        File.WriteAllText(Path.Combine(dir, "complete"), "");
    }

    static string FilePath(string dir, CursorRole role) =>
        Path.Combine(dir, role.ToString().ToLowerInvariant() + (CursorRenderer.IsAnimated(role) ? ".ani" : ".cur"));

    /// <summary>Everything the generated files depend on, so unchanged settings reuse the cached files.</summary>
    static string Fingerprint(AppConfig cfg, int baseSize)
    {
        var sb = new StringBuilder(RenderVersion).Append('|').Append(baseSize).Append('|').Append(cfg.StateCursors).Append('|');
        var s = cfg.Style;
        sb.Append($"{s.Shape}|{s.Size}|{s.Fill}|{s.Outline}|{s.OutlineWidth}|{s.Glow}|{s.GlowColor}|{s.GlowRadius}|{s.GlowStrength}|{s.Shadow}|{s.Opacity}|{s.CustomHotX}|{s.CustomHotY}|{s.HotspotDot}|{s.HotspotDotColor}|{s.HotspotDotSize}");
        var f = cfg.ClickFlash;
        if (f.Enabled) sb.Append($"|flash|{f.LeftColor}|{f.RightColor}");
        if (s.Shape == CursorShape.Custom)
        {
            try { sb.Append('|').Append(File.GetLastWriteTimeUtc(ConfigStore.CustomImagePath).Ticks); }
            catch { }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }

    static bool IsOurs(string? value) =>
        value != null && Environment.ExpandEnvironmentVariables(value).StartsWith(CursorsDir, StringComparison.OrdinalIgnoreCase);

    static void DeleteStale(string keep)
    {
        try
        {
            foreach (var d in Directory.GetDirectories(CursorsDir))
                if (!string.Equals(d, keep, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(d, recursive: true); } catch { }
        }
        catch { }
    }

    // ---- backup of the user's own values: "name<TAB>kind<TAB>value", kind "-" = value was absent ----

    record struct Saved(RegistryValueKind? Kind, string? Value);

    /// <summary>Records every value that isn't ours, so later changes by the user (new scheme, Settings) are kept.</summary>
    static Dictionary<string, Saved> UpdateBackup()
    {
        var backup = ReadBackup();
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: false);
        var names = key.GetValueNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, value) in Values)
        {
            string? current = names.Contains(value) ? key.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string : null;
            if (IsOurs(current)) continue;
            backup[value] = current == null ? new Saved(null, null) : new Saved(key.GetValueKind(value), current);
        }
        Directory.CreateDirectory(ConfigStore.Dir);
        File.WriteAllLines(BackupPath, backup.Select(kv =>
            $"{kv.Key}\t{(kv.Value.Kind is { } k ? ((int)k).ToString() : "-")}\t{kv.Value.Value}"));
        return backup;
    }

    static Dictionary<string, Saved> ReadBackup()
    {
        var d = new Dictionary<string, Saved>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(BackupPath)) return d;
            foreach (var line in File.ReadAllLines(BackupPath))
            {
                var parts = line.Split('\t', 3);
                if (parts.Length < 3) continue;
                d[parts[0]] = parts[1] == "-" ? new Saved(null, null) : new Saved((RegistryValueKind)int.Parse(parts[1]), parts[2]);
            }
        }
        catch { }
        return d;
    }

    static void RestoreValue(RegistryKey key, string value, Dictionary<string, Saved> backup)
    {
        if (!backup.TryGetValue(value, out var saved)) return;
        if (saved.Kind is { } kind && saved.Value != null) key.SetValue(value, saved.Value, kind);
        else key.DeleteValue(value, throwOnMissingValue: false);
    }
}
