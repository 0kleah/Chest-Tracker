using System.IO;
using System.Text;

namespace ChestTracker.Core;

/// <summary>
/// Reads the new lines of Fortnite's own log file (FortniteGame.log, a plain text file Fortnite writes to disk) to
/// notice a Battle Royale match starting without a replay being recorded, i.e. replays are switched off.
/// It only reads the file; it never touches the game.
/// </summary>
public sealed class FortniteLog
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Logs", "FortniteGame.log");

    /// <summary>How long after joining a match the replay must have started.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(90);

    readonly string _path;
    long _pos = -1;
    DateTime _fileCreated;
    string _partial = "";

    /// <summary>When you joined the current Battle Royale match (null in the lobby / no match seen).</summary>
    public DateTime? JoinedUtc { get; private set; }
    /// <summary>The current match is being recorded.</summary>
    public bool Recording { get; private set; }
    /// <summary>The last match you joined never started recording a replay.</summary>
    public bool LastMatchNotRecorded { get; private set; }

    public FortniteLog(string? path = null) => _path = path ?? DefaultPath;

    /// <summary>
    /// When Fortnite was opened: the log's first line says so ("Log file open, 11/12/26 20:39:28", local time).
    /// (The file's creation date can't be trusted: Windows keeps the old one when a file is replaced quickly.)
    /// </summary>
    public static DateTime? OpenedAt(string? path = null)
    {
        try
        {
            using var fs = new FileStream(path ?? DefaultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[160];
            int n = fs.Read(buf, 0, buf.Length);
            return ParseOpenLine(Encoding.UTF8.GetString(buf, 0, n));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static DateTime? ParseOpenLine(string text)
    {
        var line = text.TrimStart((char)0xFEFF).Split((char)10)[0].Trim();
        const string prefix = "Log file open, ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return null;
        return DateTime.TryParseExact(line[prefix.Length..], "MM/dd/yy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                                      System.Globalization.DateTimeStyles.AssumeLocal, out var t) ? t : null;
    }

    /// <summary>Reads what Fortnite added to the log since last time. Starts at the end of the file the first time.</summary>
    public void Poll(DateTime nowUtc)
    {
        try
        {
            var fi = new FileInfo(_path);
            if (!fi.Exists) return;
            // Fortnite starts a new log each time it opens (the old one is renamed to a backup).
            if (_pos < 0 || fi.CreationTimeUtc != _fileCreated || fi.Length < _pos)
            {
                bool first = _pos < 0;
                _fileCreated = fi.CreationTimeUtc;
                _pos = first ? fi.Length : 0;
                _partial = "";
                if (first) return;
            }
            if (fi.Length == _pos) { Check(nowUtc); return; }

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(_pos, SeekOrigin.Begin);
            var buffer = new byte[Math.Min(fi.Length - _pos, 4 * 1024 * 1024)];
            int read = fs.Read(buffer, 0, buffer.Length);
            _pos += read;
            Feed(Encoding.UTF8.GetString(buffer, 0, read), nowUtc);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Check(nowUtc);
    }

    /// <summary>Handles new log text (public for tests).</summary>
    public void Feed(string text, DateTime nowUtc)
    {
        var lines = (_partial + text).Split('\n');
        _partial = lines[^1];
        for (int i = 0; i < lines.Length - 1; i++) Line(lines[i], nowUtc);
    }

    void Line(string line, DateTime nowUtc)
    {
        if (line.Contains("Welcomed by server", StringComparison.Ordinal))
        {
            // A Battle Royale match (the BR game feature). Lobby, Creative and other modes don't say BRRoot.
            if (line.Contains("BRRoot", StringComparison.Ordinal))
            {
                JoinedUtc = nowUtc;
                Recording = false;
            }
            else JoinedUtc = null;
        }
        else if (line.Contains("RecordReplay: Starting recording", StringComparison.Ordinal))
        {
            Recording = true;
            LastMatchNotRecorded = false;
        }
        else if (line.Contains("LoadMap: /Game/Maps/Frontend", StringComparison.Ordinal))
        {
            JoinedUtc = null;   // back in the lobby
        }
    }

    /// <summary>Call regularly: has a match gone too long without recording?</summary>
    public void Check(DateTime nowUtc)
    {
        if (JoinedUtc is DateTime joined && !Recording && nowUtc - joined > Grace) LastMatchNotRecorded = true;
    }

    /// <summary>A replay file showed up (so recording works whatever the log says).</summary>
    public void ReplaySeen()
    {
        Recording = true;
        LastMatchNotRecorded = false;
    }
}
