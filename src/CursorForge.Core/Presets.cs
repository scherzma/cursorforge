namespace CursorForge;

public sealed record Preset(string Id, string Name, CursorStyle Style);

public static class Presets
{
    public static readonly IReadOnlyList<Preset> All =
    [
        new("classic", "Classic", new CursorStyle
        {
            Shape = CursorShape.Arrow, Size = 44, Fill = "#FFFFFFFF", Outline = "#FF0A0A0A", OutlineWidth = 2f, Shadow = true,
        }),
        new("midnight", "Midnight", new CursorStyle
        {
            Shape = CursorShape.Arrow, Size = 44, Fill = "#FF14161B", Outline = "#FFFFFFFF", OutlineWidth = 2f, Shadow = true,
        }),
        new("sunburst", "Sunburst", new CursorStyle
        {
            Shape = CursorShape.Arrow, Size = 48, Fill = "#FFFFD60A", Outline = "#FF151100", OutlineWidth = 2.5f, Shadow = true,
        }),
        new("neon", "Neon", new CursorStyle
        {
            Shape = CursorShape.Pointer, Size = 40, Fill = "#FF0B1020", Outline = "#FF22D3EE", OutlineWidth = 2.5f,
            Glow = true, GlowColor = "#FF22D3EE", GlowRadius = 10f, GlowStrength = 0.75f, Shadow = false,
        }),
        new("violet", "Violet", new CursorStyle
        {
            Shape = CursorShape.Pointer, Size = 42, Fill = "#FF8B6CFF", Outline = "#FFFFFFFF", OutlineWidth = 2f, Shadow = true,
        }),
        new("toxic", "Toxic", new CursorStyle
        {
            Shape = CursorShape.Arrowhead, Size = 34, Fill = "#FFB6FF3B", Outline = "#FF0B1400", OutlineWidth = 2f,
            Glow = true, GlowColor = "#FFB6FF3B", GlowRadius = 8f, GlowStrength = 0.55f, Shadow = false,
        }),
        new("hotpink", "Hot Pink", new CursorStyle
        {
            Shape = CursorShape.Arrowhead, Size = 34, Fill = "#FFFF3EA5", Outline = "#FFFFFFFF", OutlineWidth = 2f, Shadow = true,
        }),
        new("glass", "Glass", new CursorStyle
        {
            Shape = CursorShape.Arrow, Size = 44, Fill = "#55FFFFFF", Outline = "#E6FFFFFF", OutlineWidth = 1.5f, Shadow = true,
        }),
        new("ring", "Yolo Ring", new CursorStyle
        {
            Shape = CursorShape.Ring, Size = 40, Fill = "#FFFFD60A", Outline = "#FF000000", OutlineWidth = 1.5f, Shadow = true,
        }),
        new("target", "Target", new CursorStyle
        {
            Shape = CursorShape.Target, Size = 40, Fill = "#FFFF3B30", Outline = "#FFFFFFFF", OutlineWidth = 1.5f, Shadow = true,
        }),
        new("dot", "Laser Dot", new CursorStyle
        {
            Shape = CursorShape.Dot, Size = 16, Fill = "#FFFF3B30", Outline = "#FFFFFFFF", OutlineWidth = 2f,
            Glow = true, GlowColor = "#FFFF3B30", GlowRadius = 7f, GlowStrength = 0.6f, Shadow = false,
        }),
        new("halo", "Halo", new CursorStyle
        {
            Shape = CursorShape.Halo, Size = 56, Fill = "#38FFE45C", Outline = "#FFFFE45C", OutlineWidth = 2f, Shadow = false,
        }),
        new("cross", "Precision", new CursorStyle
        {
            Shape = CursorShape.Cross, Size = 36, Fill = "#FF00FF9C", Outline = "#FF001A10", OutlineWidth = 1.5f, Shadow = true,
        }),
        new("diamond", "Diamond", new CursorStyle
        {
            Shape = CursorShape.Diamond, Size = 28, Fill = "#FF8B6CFF", Outline = "#FFFFFFFF", OutlineWidth = 2f,
            Glow = true, GlowColor = "#FF8B6CFF", GlowRadius = 8f, GlowStrength = 0.6f, Shadow = false,
        }),
    ];

    public static Preset Default => All[0];

    public static Preset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);
}
