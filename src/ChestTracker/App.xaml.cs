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
        bool second = false;
#if DEBUG
        second = e.Args.Contains("--second-instance");   // development checks only (Debug builds)
        Quiet = e.Args.Contains("--offscreen");
#endif
        if (!first && !second)
        {
            // Already running (maybe hidden in the tray): bring that one up instead of starting a second copy.
            bool told = false;
            try { AllowSetForegroundWindow(-1); using var show = EventWaitHandle.OpenExisting(ShowEventName); told = show.Set(); }
            catch (Exception) { }
            if (!told) MessageBox.Show("Chest Tracker is already running.", "Chest Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => LogCrash(a.ExceptionObject as Exception, "background thread");
        TaskScheduler.UnobservedTaskException += (_, a) => { LogCrash(a.Exception, "background task"); a.SetObserved(); };

        // Everything is saved next to the .exe, so the folder has to be one the app can write to.
        if (!Core.DataFile.CanWrite(AppState.DataDir))
        {
            Tell($"Chest Tracker can't save anything in this folder:\n\n{AppState.DataDir}\n\n" +
                 "Move ChestTracker.exe to a folder of its own that you can write to (for example in Documents or on the Desktop), " +
                 "not Program Files or inside a .zip, and start it again.", MessageBoxImage.Warning);
            Shutdown();
            return;
        }
        try { State = new AppState(); }
        catch (DataBusyException busy)
        {
            Tell($"Chest Tracker couldn't open its saved file {busy.FileName} because another program is using it " +
                 "(a sync or backup program, or an antivirus scan).\n\nNothing was changed. Wait a moment and start Chest Tracker again.",
                 MessageBoxImage.Warning);
            Shutdown();
            return;
        }
        ThemeManager.Apply(State.Settings);   // your colours, before any window is made
        State.Tracker.StartInputPolling();

        var main = new MainWindow();
        MainWindow = main;
#if DEBUG
        if (Quiet) main.GoOffscreen();
#endif
        if (e.Args.Contains("--tray")) main.StartHidden();   // started with Windows: stay in the tray until opened
        else main.Show();
        if (first) ListenForShow(main);
        if (State.StartupNotes.Count > 0)
            Tell(string.Join("\n\n", State.StartupNotes) + $"\n\nBackups are in the \"{Core.DataFile.BackupFolder}\" folder next to ChestTracker.exe.", MessageBoxImage.Information);
    }

    /// <summary>Development checks only: no pop-up messages (they would appear over whatever is on screen).</summary>
    public static bool Quiet { get; private set; }

    static void Tell(string text, MessageBoxImage icon)
    {
        if (Quiet) return;
        MessageBox.Show(text, "Chest Tracker", MessageBoxButton.OK, icon);
    }

    static readonly object CrashGate = new();

    /// <summary>Adds an error to crash.log next to the app (the newest at the end; the file is kept small).</summary>
    static void LogCrash(Exception? ex, string where)
    {
        if (ex is null) return;
        try
        {
            lock (CrashGate)
            {
                var path = Path.Combine(AppState.DataDir, "crash.log");
                string old = File.Exists(path) ? File.ReadAllText(path) : "";
                if (old.Length > 60_000) old = old[^30_000..];
                var version = typeof(App).Assembly.GetName().Version;
                File.WriteAllText(path, old + $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Chest Tracker {version} ({where}){Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception) { }
    }

    const string ShowEventName = "ChestTracker.Show";

    /// <summary>Lets the copy that's already running come to the front (Windows only allows it when the new copy says so).</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Opening Chest Tracker again while it's running brings this window to the front.</summary>
    void ListenForShow(MainWindow main)
    {
        EventWaitHandle show;
        try { show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName); }
        catch (Exception) { return; }
        new Thread(() =>
        {
            while (show.WaitOne())
                Dispatcher.BeginInvoke(main.BringToFront);
        }) { IsBackground = true, Name = "ShowRequests" }.Start();
    }

    int _errorsShown;

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "window");
        e.Handled = true;
        // Say so once; the same problem repeating (on a timer, say) mustn't bury the screen in messages.
        if (Interlocked.Increment(ref _errorsShown) == 1)
            Tell("Something went wrong:\n\n" + e.Exception.Message + "\n\nChest Tracker will keep running. Details were saved to crash.log next to the app; " +
                 "if it keeps happening, please report it on the GitHub page with that file.", MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        State?.Tracker.Stop();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
