using System.Diagnostics;
using System.Windows;

namespace CursorForge.Ui;

public partial class App : Application
{
    Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Release builds are a single downloadable exe that installs itself (see Installer).
        if (!Installer.Run(e.Args))
        {
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, Ipc.UiMutex, out bool owned);
        if (!owned)
        {
            FocusExistingInstance();
            Shutdown();
            return;
        }
        base.OnStartup(e);

        var cfg = ConfigStore.Load();
        ConfigStore.Save(cfg); // persists defaults on first run and any settings migration
        // Keep the autostart entry pointing at wherever the app lives now (e.g. after moving the folder).
        if (cfg.StartWithWindows) Autostart.Set(true);
        AgentClient.EnsureRunning();

        new MainWindow(cfg).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }

    static void FocusExistingInstance()
    {
        using var self = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(self.ProcessName))
        {
            using (p)
            {
                if (p.Id == self.Id || p.MainWindowHandle == 0) continue;
                NativeUi.ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
                NativeUi.SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }
    }
}
