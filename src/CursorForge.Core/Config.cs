using System.Text.Json.Serialization;

namespace CursorForge;

public enum CursorShape { Arrow, Pointer, Arrowhead, Triangle, Ring, Dot, Target, Cross, Diamond, Halo, Custom }

/// <summary>The Windows cursor states CursorForge can draw a matching cursor for.</summary>
public enum CursorRole
{
    Pointer, Link, Text, Busy, Working, Unavailable, Precision, Move,
    SizeNS, SizeWE, SizeNWSE, SizeNESW, Up, Help, Pin, Person,
}

public enum StateCursorMode
{
    /// <summary>Each state gets its own glyph (hand, I-beam, resize arrows...) in the chosen style.</summary>
    Matching,
    /// <summary>Every replaced state shows the main pointer.</summary>
    Same,
}

/// <summary>How the overlay behaves for an app that draws its own cursor.</summary>
public enum OverlayMode
{
    /// <summary>Show only while the app shows a (non-system) OS cursor. Safe for shooters.</summary>
    Auto,
    /// <summary>Also show while the OS cursor is hidden (software-cursor games), unless the cursor is locked.</summary>
    Always,
}

public sealed class CursorStyle
{
    public CursorShape Shape { get; set; } = CursorShape.Arrow;
    /// <summary>Nominal shape size in physical pixels (height for pointers, diameter for round shapes).</summary>
    public int Size { get; set; } = 44;
    public string Fill { get; set; } = "#FFFFFFFF";
    public string Outline { get; set; } = "#FF0A0A0A";
    public float OutlineWidth { get; set; } = 2f;
    public bool Glow { get; set; }
    public string GlowColor { get; set; } = "#FF22D3EE";
    public float GlowRadius { get; set; } = 8f;
    public float GlowStrength { get; set; } = 0.7f;
    public bool Shadow { get; set; } = true;
    public float Opacity { get; set; } = 1f;
    /// <summary>Marks the hotspot (the exact pixel a click lands on) with a single opaque pixel.</summary>
    public bool HotspotDot { get; set; }
    public string HotspotDotColor { get; set; } = "#FFFF3B30";
    /// <summary>Dot diameter in pixels; 1 = exactly the hotspot pixel.</summary>
    public float HotspotDotSize { get; set; } = 3;
    /// <summary>Hotspot of a custom image as a fraction of its size (0..1).</summary>
    public float CustomHotX { get; set; }
    public float CustomHotY { get; set; }

    /// <summary>
    /// Per-state icon choice, keyed by <see cref="CursorRole"/> name: "Pointer" (the main pointer) or a
    /// <see cref="CursorShape"/> name. Missing = the state's own matching glyph.
    /// </summary>
    public Dictionary<string, string> StateGlyphs { get; set; } = [];

    /// <summary>Special <see cref="Fill"/> value: the fill shows the inverse of whatever is behind the cursor.</summary>
    public const string Invert = "Invert";
    public const string GlyphDefault = "Default", GlyphPointer = "Pointer";

    /// <summary>Inverted fill (XOR with the screen). Not for imported images.</summary>
    [JsonIgnore] public bool IsInverted => string.Equals(Fill, Invert, StringComparison.OrdinalIgnoreCase) && Shape != CursorShape.Custom;

    public string GlyphFor(CursorRole role) =>
        StateGlyphs != null && StateGlyphs.TryGetValue(role.ToString(), out var g) ? g : GlyphDefault;

    /// <summary>
    /// A normal (alpha-blended) stand-in for places that can't invert what's behind them: UI previews,
    /// the tray icon and the game overlay.
    /// </summary>
    public CursorStyle ForDisplay(bool darkBackground)
    {
        if (!IsInverted) return this;
        var s = Clone();
        s.Fill = darkBackground ? "#FFEDEDED" : "#FF141414";
        return s;
    }

    public CursorStyle Clone()
    {
        var c = (CursorStyle)MemberwiseClone();
        c.StateGlyphs = new Dictionary<string, string>(StateGlyphs ?? []);
        return c;
    }

    internal void Normalize()
    {
        Size = Math.Clamp(Size, 8, 200);
        OutlineWidth = Math.Clamp(OutlineWidth, 0, 8);
        GlowRadius = Math.Clamp(GlowRadius, 1, 24);
        GlowStrength = Math.Clamp(GlowStrength, 0, 1);
        Opacity = Math.Clamp(Opacity, 0.1f, 1);
        CustomHotX = Math.Clamp(CustomHotX, 0, 1);
        CustomHotY = Math.Clamp(CustomHotY, 0, 1);
        Fill = string.Equals(Fill, Invert, StringComparison.OrdinalIgnoreCase) ? Invert
            : Rgba.TryParse(Fill, out _) ? Fill : "#FFFFFFFF";
        StateGlyphs ??= [];
        foreach (var key in StateGlyphs.Keys.ToList())
        {
            string v = StateGlyphs[key];
            bool validKey = Enum.TryParse<CursorRole>(key, out var role) && role != CursorRole.Pointer;
            bool validValue = v == GlyphPointer || (Enum.TryParse<CursorShape>(v, out var sh) && sh != CursorShape.Custom);
            if (!validKey || !validValue) StateGlyphs.Remove(key);
        }
        Outline = Rgba.TryParse(Outline, out _) ? Outline : "#FF000000";
        GlowColor = Rgba.TryParse(GlowColor, out _) ? GlowColor : "#FF22D3EE";
        HotspotDotColor = Rgba.TryParse(HotspotDotColor, out _) ? HotspotDotColor : "#FFFF3B30";
        HotspotDotSize = Math.Clamp(HotspotDotSize, 1, 12);
    }
}

public sealed class OverlayApp
{
    /// <summary>Process name without ".exe".</summary>
    public string Process { get; set; } = "";
    public OverlayMode Mode { get; set; } = OverlayMode.Auto;

    public bool Matches(string processName) =>
        string.Equals(Normalize(processName), Process, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string name)
    {
        name = name.Trim().Trim('"');
        int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (slash >= 0) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Trim();
    }
}

public sealed class ClickFlash
{
    public bool Enabled { get; set; }
    public string LeftColor { get; set; } = "#FFFFD60A";
    public string RightColor { get; set; } = "#FF22D3EE";
    /// <summary>Minimum time the flash stays visible; it also lasts as long as the button is held.</summary>
    public int DurationMs { get; set; } = 150;

    public ClickFlash Clone() => (ClickFlash)MemberwiseClone();

    internal void Normalize()
    {
        DurationMs = Math.Clamp(DurationMs, 50, 1000);
        LeftColor = Rgba.TryParse(LeftColor, out _) ? LeftColor : "#FFFFD60A";
        RightColor = Rgba.TryParse(RightColor, out _) ? RightColor : "#FF22D3EE";
    }

    /// <summary>The cursor style shown while the given button is down.</summary>
    public CursorStyle Apply(CursorStyle style, bool right)
    {
        var s = style.Clone();
        s.Fill = right ? RightColor : LeftColor;
        if (s.Glow) s.GlowColor = s.Fill;
        return s;
    }
}

/// <summary>A look the user saved: style plus the settings that belong to how the cursor looks.</summary>
public sealed class SavedPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public CursorStyle Style { get; set; } = new();
    public ClickFlash ClickFlash { get; set; } = new();
    public StateCursorMode StateCursors { get; set; } = StateCursorMode.Matching;

    /// <summary>Value stored in <see cref="AppConfig.PresetId"/> while this preset is applied.</summary>
    [JsonIgnore] public string Key => "user:" + Id;

    /// <summary>Where a saved preset keeps its own copy of an imported image.</summary>
    [JsonIgnore] public string ImagePath => Path.Combine(ConfigStore.Dir, "presets", Id + ".cfimg");
}

public sealed class Hotkey
{
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;

    /// <summary>MOD_* flags as used by RegisterHotKey.</summary>
    public uint Modifiers { get; set; }
    /// <summary>Virtual-key code; 0 = disabled.</summary>
    public uint Key { get; set; }
}

public sealed class AppConfig
{
    const int CurrentVersion = 2;

    public int Version { get; set; }
    public bool Enabled { get; set; } = true;
    public string PresetId { get; set; } = Presets.Default.Id;
    public CursorStyle Style { get; set; } = Presets.Default.Style.Clone();

    public bool ReplaceArrow { get; set; } = true;
    public bool ReplaceHand { get; set; } = true;
    public bool ReplaceBusy { get; set; } = true;
    public bool ReplaceText { get; set; } = true;
    public bool ReplaceResize { get; set; } = true;
    public bool ReplaceOther { get; set; } = true;
    public StateCursorMode StateCursors { get; set; } = StateCursorMode.Matching;

    public ClickFlash ClickFlash { get; set; } = new();
    public List<SavedPreset> SavedPresets { get; set; } = [];

    public bool OverlayEnabled { get; set; } = true;
    public List<OverlayApp> OverlayApps { get; set; } = [];

    public bool StartWithWindows { get; set; } = true;
    public Hotkey ToggleHotkey { get; set; } = new() { Modifiers = Hotkey.Control | Hotkey.Alt | Hotkey.Shift, Key = 0x43 /* C */ };
    public Hotkey OverlayHotkey { get; set; } = new() { Modifiers = Hotkey.Control | Hotkey.Alt | Hotkey.Shift, Key = 0x4F /* O */ };

    internal void Normalize()
    {
        if (Version < 2)
        {
            // v1 replaced every state with the pointer image, so text/resize/other were off by default.
            // Now that each state has a matching cursor, replace the whole set.
            ReplaceText = ReplaceResize = ReplaceOther = true;
            StateCursors = StateCursorMode.Matching;
        }
        Version = CurrentVersion;
        Style ??= Presets.Default.Style.Clone();
        OverlayApps ??= [];
        ToggleHotkey ??= new();
        OverlayHotkey ??= new();
        ClickFlash ??= new();
        ClickFlash.Normalize();
        PresetId ??= "";
        Style.Normalize();
        SavedPresets ??= [];
        SavedPresets.RemoveAll(p => p is null || p.Style is null);
        foreach (var p in SavedPresets)
        {
            p.Style.Normalize();
            p.ClickFlash ??= new();
            p.ClickFlash.Normalize();
            p.Name = string.IsNullOrWhiteSpace(p.Name) ? "My cursor" : p.Name.Trim();
            if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N");
        }
        OverlayApps.RemoveAll(a => a is null || string.IsNullOrWhiteSpace(a.Process));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;
