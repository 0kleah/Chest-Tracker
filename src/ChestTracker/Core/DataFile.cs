using System.Globalization;
using System.IO;

namespace ChestTracker.Core;

/// <summary>What happened when a saved file was opened.</summary>
public enum LoadState
{
    /// <summary>There was no file: a first run.</summary>
    New,
    Ok,
    /// <summary>The file couldn't be understood; it was set aside and the newest good backup was put back.</summary>
    RestoredFromBackup,
    /// <summary>The file couldn't be understood and there was no good backup; it was set aside and a new one started.</summary>
    Unreadable,
    /// <summary>The file couldn't be opened (another program is holding it, or Windows refused). Nothing was changed.</summary>
    Busy,
}

/// <summary>A loaded value, how the loading went, and where an unreadable file was moved to (if it was).</summary>
public sealed record Loaded<T>(T Value, LoadState State, string? SetAside = null, string? BackupUsed = null);

/// <summary>
/// Reading and writing the app's saved files so that nothing is ever lost quietly:
///  - a file that can't be opened right now (a sync or antivirus program is holding it) is retried, and if it still
///    can't be opened the app is told, instead of carrying on with an empty count and later saving over the real one;
///  - a file that can't be understood (damaged) is moved aside, never overwritten, and the newest good backup is put back;
///  - saving writes a new file next to the old one and swaps them, with a few retries, so a crash or power cut
///    mid-save can't leave half a file;
///  - a copy of each good file is kept in the "backups" folder, one per day for the last few days.
/// </summary>
public static class DataFile
{
    public const string BackupFolder = "backups";
    const int KeepDays = 4;

    static readonly object Gate = new();
    static string? _saveProblem;

    /// <summary>Why the last save failed (null when saving works). Shown to the user until a save works again.</summary>
    public static string? SaveProblem { get { lock (Gate) return _saveProblem; } }
    /// <summary>Raised (on any thread) when <see cref="SaveProblem"/> changes.</summary>
    public static event Action? SaveProblemChanged;

    static void SetProblem(string? problem)
    {
        bool changed;
        lock (Gate) { changed = _saveProblem != problem; _saveProblem = problem; }
        if (changed) SaveProblemChanged?.Invoke();
    }

    static bool IsBusy(Exception e) => e is IOException or UnauthorizedAccessException;

    /// <summary>Reads a file, waiting a little if another program is using it. Null if it can't be opened.</summary>
    static string? ReadText(string path, int attempts = 6)
    {
        for (int i = 1; ; i++)
        {
            try { return File.ReadAllText(path); }
            catch (Exception e) when (IsBusy(e))
            {
                if (e is FileNotFoundException or DirectoryNotFoundException) return null;
                if (i >= attempts) return null;
                Thread.Sleep(200);
            }
        }
    }

    static string BackupDir(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, BackupFolder);

    /// <summary>The backups of a file, newest first.</summary>
    public static List<string> Backups(string path)
    {
        try
        {
            var dir = BackupDir(path);
            if (!Directory.Exists(dir)) return new();
            string stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            return Directory.GetFiles(dir, stem + ".*" + ext)
                .Where(f => Path.GetFileName(f).Length == stem.Length + 1 + 10 + ext.Length)   // stem.yyyy-MM-dd.ext
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (IsBusy(e)) { return new(); }
    }

    /// <summary>
    /// Opens a saved file. <paramref name="parse"/> returns null (or throws) for text it can't understand.
    /// Never overwrites or deletes anything it couldn't read.
    /// </summary>
    public static Loaded<T> Load<T>(string path, Func<string, T?> parse, Func<T> fresh) where T : class
    {
        if (!File.Exists(path)) return new Loaded<T>(fresh(), LoadState.New);
        var text = ReadText(path);
        if (text is null)
            return File.Exists(path) ? new Loaded<T>(fresh(), LoadState.Busy) : new Loaded<T>(fresh(), LoadState.New);
        if (TryParse(text, parse) is { } value) return new Loaded<T>(value, LoadState.Ok);

        // Damaged: move it out of the way (so it's never overwritten), then put the newest good backup back.
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string aside = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}.unreadable-{stamp}{Path.GetExtension(path)}");
        for (int n = 2; File.Exists(aside) && n < 100; n++)
            aside = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}.unreadable-{stamp}-{n}{Path.GetExtension(path)}");
        try { File.Move(path, aside, overwrite: false); }
        catch (Exception e) when (IsBusy(e)) { return new Loaded<T>(fresh(), LoadState.Busy); }

        foreach (var backup in Backups(path))
        {
            var bt = ReadText(backup, attempts: 2);
            if (bt is null || TryParse(bt, parse) is not { } restored) continue;
            try { File.Copy(backup, path, overwrite: false); } catch (Exception e) when (IsBusy(e)) { }
            return new Loaded<T>(restored, LoadState.RestoredFromBackup, Path.GetFileName(aside), Path.GetFileName(backup));
        }
        return new Loaded<T>(fresh(), LoadState.Unreadable, Path.GetFileName(aside));
    }

    static T? TryParse<T>(string text, Func<string, T?> parse) where T : class
    {
        try { return parse(text); }
        catch (Exception) { return null; }   // whatever the parser throws, the file is not usable
    }

    /// <summary>
    /// Saves text to a file: written next to it first, then swapped in, retried a few times if another program is in
    /// the way. Returns false (and sets <see cref="SaveProblem"/>) if it couldn't be saved; the caller keeps its data
    /// in memory and the next save tries again.
    /// </summary>
    public static bool Save(string path, string text)
    {
        var tmp = path + ".tmp";
        Exception? last = null;
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, text);
                File.Move(tmp, path, overwrite: true);
                SetProblem(null);
                return true;
            }
            catch (Exception e) when (IsBusy(e))
            {
                last = e;
                if (attempt < 4) Thread.Sleep(150 * attempt);
            }
        }
        SetProblem($"Couldn't save {Path.GetFileName(path)}: {last?.Message}");
        return false;
    }

    /// <summary>
    /// Keeps today's copy of a good file in the backups folder (once a day; the last few days are kept).
    /// Call after the file loaded fine. Failures are ignored: a backup must never get in the way.
    /// </summary>
    public static void Backup(string path, DateTime today)
    {
        try
        {
            if (!File.Exists(path)) return;
            var dir = BackupDir(path);
            string stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            string target = Path.Combine(dir, $"{stem}.{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{ext}");
            if (File.Exists(target)) return;   // today's copy is already there (the state at the first start of the day)
            Directory.CreateDirectory(dir);
            File.Copy(path, target, overwrite: false);
            foreach (var old in Backups(path).Skip(KeepDays))
                try { File.Delete(old); } catch (Exception e) when (IsBusy(e)) { }
        }
        catch (Exception e) when (IsBusy(e)) { }
    }

    /// <summary>Can files be made in this folder? (False for a read-only place such as Program Files.)</summary>
    public static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $"chest_tracker_write_test_{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (IsBusy(e)) { return false; }
    }
}
