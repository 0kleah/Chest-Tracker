using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ChestTracker;

public partial class App : Application
{
    Mutex? _singleInstance;

    public AppState State { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "ChestTracker.SingleInstance", out bool first);
        if (!first && !e.Args.Contains("--second-instance"))   // the flag is for development checks only
        {
            MessageBox.Show("Chest Tracker is already running.", "Chest Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        State = new AppState();
        ThemeManager.Apply(State.Settings);   // your colours, before any window is made
        State.Tracker.StartInputPolling();

        var main = new MainWindow();
        MainWindow = main;
        if (e.Args.Contains("--tray")) main.StartHidden();   // started with Windows: stay in the tray
        main.Show();
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try { File.WriteAllText(Path.Combine(AppState.DataDir, "crash.log"), e.Exception.ToString()); } catch (Exception) { }
        MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message + "\n\nDetails were saved to crash.log next to the app.",
                        "Chest Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        State?.Tracker.Stop();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
