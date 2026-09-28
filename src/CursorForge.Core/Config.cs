using System.Text.Json.Serialization;

namespace CursorForge;

public enum CursorShape { Arrow, Pointer, Arrowhead, Ring, Dot, Target, Cross, Diamond, Halo, Custom }

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
    /// <summary>Hotspot of a custom image as a fraction of its size (0..1).</summary>
    public float CustomHotX { get; set; }
    public float CustomHotY { get; set; }

    public CursorStyle Clone() => (CursorStyle)MemberwiseClone();
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
        PresetId ??= "";
        var s = Style;
        s.Size = Math.Clamp(s.Size, 8, 200);
        s.OutlineWidth = Math.Clamp(s.OutlineWidth, 0, 8);
        s.GlowRadius = Math.Clamp(s.GlowRadius, 1, 24);
        s.GlowStrength = Math.Clamp(s.GlowStrength, 0, 1);
        s.Opacity = Math.Clamp(s.Opacity, 0.1f, 1);
        s.CustomHotX = Math.Clamp(s.CustomHotX, 0, 1);
        s.CustomHotY = Math.Clamp(s.CustomHotY, 0, 1);
        s.Fill = Rgba.TryParse(s.Fill, out _) ? s.Fill : "#FFFFFFFF";
        s.Outline = Rgba.TryParse(s.Outline, out _) ? s.Outline : "#FF000000";
        s.GlowColor = Rgba.TryParse(s.GlowColor, out _) ? s.GlowColor : "#FF22D3EE";
        OverlayApps.RemoveAll(a => a is null || string.IsNullOrWhiteSpace(a.Process));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;
