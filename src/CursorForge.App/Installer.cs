using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace CursorForge.Ui;

/// <summary>
/// Single-exe distribution. The release build embeds the agent; running the downloaded exe installs it for the
/// current user (no admin): %LOCALAPPDATA%\Programs\CursorForge, a Start-menu shortcut and an entry in
/// Settings &gt; Apps for uninstalling. Developer builds (agent already next to the exe) skip all of this.
/// </summary>
internal static class Installer
{
    const string ResourceName = "CursorForge.Agent.exe";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CursorForge";

    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CursorForge");

    static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "CursorForge.lnk");

    static bool HasEmbeddedAgent => typeof(Installer).Assembly.GetManifestResourceInfo(ResourceName) != null;

    /// <summary>True when running the installed copy of a release build.</summary>
    public static bool IsInstalledCopy =>
        HasEmbeddedAgent && SamePath(Path.GetDirectoryName(Environment.ProcessPath!)!, InstallDir);

    /// <summary>Returns false when this process should exit (handed over to the installed copy, or cancelled).</summary>
    public static bool Run(string[] args)
    {
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            Uninstall(askFirst: true);
            return false;
        }
        if (!HasEmbeddedAgent) return true;

        string exe = Environment.ProcessPath!;
        string here = Path.GetDirectoryName(exe)!;
        if (SamePath(here, InstallDir))
        {
            ExtractAgent(here); // keeps the agent in step with this exe after an update
            return true;
        }

        var answer = MessageBox.Show(
            "Install CursorForge for your account?\n\n" +
            $"It will be copied to {InstallDir}, added to the Start menu and started with Windows. " +
            "No admin rights needed; you can uninstall it any time from Settings > Apps.\n\n" +
            "Choose “No” to run it portable from this folder instead.",
            "CursorForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No)
        {
            ExtractAgent(here);
            return true;
        }

        try
        {
            CloseInstalledUi();
            Directory.CreateDirectory(InstallDir);
            string target = Path.Combine(InstallDir, Ipc.UiExe);
            Retry(() => File.Copy(exe, target, overwrite: true));
            ExtractAgent(InstallDir);
            CreateShortcut(target);
            RegisterUninstall(target);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = InstallDir })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Installing failed:\n" + ex.Message, "CursorForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return false;
    }

    /// <summary>Restores the Windows cursors and removes everything the installer added (settings are kept).</summary>
    public static void Uninstall(bool askFirst)
    {
        if (askFirst && MessageBox.Show(
                "Uninstall CursorForge?\n\nYour normal Windows cursors come back right away. " +
                $"Your settings stay in {ConfigStore.Dir} in case you reinstall.",
                "CursorForge", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        string agent = Path.Combine(InstallDir, Ipc.AgentExe);
        StopAgent();
        // Not running (or already stopped): the agent's --exit still restores the user's cursor scheme.
        if (File.Exists(agent))
        {
            try { Process.Start(new ProcessStartInfo(agent, "--exit") { UseShellExecute = false })?.WaitForExit(5000); }
            catch { }
        }
        Autostart.Set(false);
        try { File.Delete(ShortcutPath); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }

        // The install folder may contain this very exe: delete it a moment after we exit.
        if (Directory.Exists(InstallDir))
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallDir}\"")
            { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
        }
        MessageBox.Show("CursorForge was uninstalled.", "CursorForge", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    static void ExtractAgent(string dir)
    {
        using var res = typeof(Installer).Assembly.GetManifestResourceStream(ResourceName);
        if (res == null) return;
        using var ms = new MemoryStream();
        res.CopyTo(ms);
        byte[] data = ms.ToArray();

        string path = Path.Combine(dir, Ipc.AgentExe);
        try
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(data)) return;
        }
        catch { }
        StopAgent(); // an older agent may be running from this file
        Retry(() => File.WriteAllBytes(path, data));
    }

    static void StopAgent()
    {
        AgentClient.Stop();
        for (int i = 0; i < 30 && AgentClient.IsRunning; i++) Thread.Sleep(100);
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Ipc.AgentExe)))
        {
            using (p)
            {
                try { p.WaitForExit(2000); } catch { }
            }
        }
    }

    /// <summary>An older installed settings window would keep its exe locked during an update.</summary>
    static void CloseInstalledUi()
    {
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Ipc.UiExe)))
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || p.MainModule?.FileName is not { } file || !SamePath(Path.GetDirectoryName(file)!, InstallDir)) continue;
                    p.CloseMainWindow();
                    if (!p.WaitForExit(3000)) p.Kill();
                }
                catch { }
            }
        }
    }

    static void CreateShortcut(string target)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = target;
            link.WorkingDirectory = InstallDir;
            link.IconLocation = target + ",0";
            link.Description = "Custom cursors with zero added latency";
            link.Save();
        }
        catch { }
    }

    static void RegisterUninstall(string target)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            long sizeKb = new DirectoryInfo(InstallDir).EnumerateFiles().Sum(f => f.Length) / 1024;
            key.SetValue("DisplayName", "CursorForge");
            key.SetValue("DisplayVersion", version == null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}");
            key.SetValue("Publisher", "CursorForge");
            key.SetValue("DisplayIcon", target + ",0");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{target}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
        }
        catch { }
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(150); }
            catch (UnauthorizedAccessException) when (attempt < 20) { Thread.Sleep(150); }
        }
    }
}
