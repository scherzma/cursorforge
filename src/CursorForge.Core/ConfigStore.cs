using System.Text.Json;

namespace CursorForge;

public static class ConfigStore
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CursorForge");

    public static string ConfigPath => Path.Combine(Dir, "config.json");
    public static string CustomImagePath => Path.Combine(Dir, "custom.cfimg");

    public static bool Exists => File.Exists(ConfigPath);

    public static AppConfig Load()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!File.Exists(ConfigPath)) break;
                var cfg = JsonSerializer.Deserialize(File.ReadAllText(ConfigPath), ConfigJsonContext.Default.AppConfig);
                if (cfg is null) break;
                cfg.Normalize();
                return cfg;
            }
            catch (IOException)
            {
                Thread.Sleep(30); // file being replaced by the other process
            }
            catch
            {
                break; // corrupt file -> defaults
            }
        }
        var fresh = new AppConfig();
        fresh.Normalize();
        return fresh;
    }

    public static string Serialize(AppConfig cfg) =>
        JsonSerializer.Serialize(cfg, ConfigJsonContext.Default.AppConfig);

    public static void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(Dir);
        string json = Serialize(cfg);
        string tmp = ConfigPath + "." + Environment.ProcessId + ".tmp";
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, ConfigPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(30);
            }
        }
    }
}

public static class Ipc
{
    public const string AgentWindowClass = "CursorForge.Agent";
    public const string AgentMutex = @"Local\CursorForge.Agent";
    public const string UiMutex = @"Local\CursorForge.UI";
    public const string AgentExe = "CursorForge.Agent.exe";
    public const string UiExe = "CursorForge.exe";

    public const uint WM_APP = 0x8000;
    /// <summary>Re-read config.json and re-apply everything.</summary>
    public const uint MsgReload = WM_APP + 1;
    /// <summary>Restore the Windows cursors and quit.</summary>
    public const uint MsgExit = WM_APP + 2;
}

/// <summary>OCR_* ids of the system cursors and the state each one shows, grouped the way the UI exposes them.</summary>
public static class CursorRoles
{
    public static readonly (CursorRole Role, uint Id)[] Arrow = [(CursorRole.Pointer, 32512)];
    public static readonly (CursorRole Role, uint Id)[] Hand = [(CursorRole.Link, 32649)];
    public static readonly (CursorRole Role, uint Id)[] Busy = [(CursorRole.Busy, 32514), (CursorRole.Working, 32650)];
    public static readonly (CursorRole Role, uint Id)[] Text = [(CursorRole.Text, 32513)];
    public static readonly (CursorRole Role, uint Id)[] Resize =
    [
        (CursorRole.Move, 32646), (CursorRole.SizeNESW, 32643), (CursorRole.SizeNS, 32645),
        (CursorRole.SizeNWSE, 32642), (CursorRole.SizeWE, 32644),
    ];
    public static readonly (CursorRole Role, uint Id)[] Other =
    [
        (CursorRole.Precision, 32515), (CursorRole.Unavailable, 32648), (CursorRole.Up, 32516),
        (CursorRole.Help, 32651), (CursorRole.Pin, 32671), (CursorRole.Person, 32672),
    ];

    public static readonly (CursorRole Role, uint Id)[] All = [.. Arrow, .. Hand, .. Busy, .. Text, .. Resize, .. Other];

    public static List<(CursorRole Role, uint Id)> Selected(AppConfig c)
    {
        var list = new List<(CursorRole, uint)>();
        if (c.ReplaceArrow) list.AddRange(Arrow);
        if (c.ReplaceHand) list.AddRange(Hand);
        if (c.ReplaceBusy) list.AddRange(Busy);
        if (c.ReplaceText) list.AddRange(Text);
        if (c.ReplaceResize) list.AddRange(Resize);
        if (c.ReplaceOther) list.AddRange(Other);
        return list;
    }
}
