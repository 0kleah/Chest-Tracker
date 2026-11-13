using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChestTracker.Core;

public sealed record ReleaseInfo(Version Version, string Name, string Url);
public sealed record StatusPart(string Name, string Status);
public sealed record StatusNotice(string Title, string Detail);
public sealed record FortniteStatus(string Overall, List<StatusPart> Parts, List<StatusNotice> Notices, DateTime FetchedUtc);

/// <summary>
/// The few things the app looks up online, all read-only and anonymous (nothing about you is sent):
///  - this project's latest release on GitHub (update check) and its treasure.json (Treasure Hours, odds, prizes);
///  - Epic's public status page (is Fortnite up?);
///  - fortnite-api.com's map picture and place names (for the chest map).
/// </summary>
public static class Online
{
    public const string Repo = "0kleah/Chest-Tracker";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
    const string StatusUrl = "https://status.epicgames.com/api/v2/summary.json";

    static readonly Lazy<HttpClient> Client = new(() =>
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Chest-Tracker/" + (typeof(Online).Assembly.GetName().Version?.ToString(2) ?? "3"));
        return c;
    });

    public static async Task<string?> GetStringAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await Client.Value.GetAsync(url, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { return null; }
    }

    public static async Task<bool> DownloadAsync(string url, string path, CancellationToken ct = default)
    {
        try
        {
            using var resp = await Client.Value.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;
            var tmp = path + ".tmp";
            await using (var fs = File.Create(tmp)) await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { return false; }
    }

    // ------------------------------------------------------------------ update check

    /// <summary>Reads "3.1" out of a release name like "Chest Tracker 3.1".</summary>
    public static Version? VersionIn(string? text)
    {
        var m = Regex.Match(text ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                                       m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0) : null;
    }

    public static async Task<ReleaseInfo?> LatestReleaseAsync(CancellationToken ct = default)
    {
        var json = await GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct).ConfigureAwait(false);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
            var v = VersionIn(name);
            return v is null ? null : new ReleaseInfo(v, name, url);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Is this release newer than the running version (compared as major.minor.build)?</summary>
    public static bool IsNewer(Version latest, Version? current)
    {
        if (current is null) return false;
        var cur = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        return latest > cur;
    }

    // ------------------------------------------------------------------ map

    public static List<Poi> ParsePois(string json)
    {
        var list = new List<Poi>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            if (!data.TryGetProperty("pois", out var pois) || pois.ValueKind != JsonValueKind.Array) return list;
            foreach (var p in pois.EnumerateArray())
            {
                string name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.Length == 0 || !p.TryGetProperty("location", out var loc)) continue;
                list.Add(new Poi(name, loc.GetProperty("x").GetDouble(), loc.GetProperty("y").GetDouble()));
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { }
        return list;
    }

    // ------------------------------------------------------------------ Fortnite status (Epic's public status page)

    public static async Task<FortniteStatus?> StatusAsync(CancellationToken ct = default)
    {
        var json = await GetStringAsync(StatusUrl, ct).ConfigureAwait(false);
        return json is null ? null : ParseStatus(json, DateTime.UtcNow);
    }

    public static FortniteStatus? ParseStatus(string json, DateTime nowUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var comps = root.GetProperty("components").EnumerateArray().ToList();
            var group = comps.FirstOrDefault(c => c.GetProperty("name").GetString() == "Fortnite"
                                                  && c.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.True);
            string groupId = group.ValueKind == JsonValueKind.Object ? group.GetProperty("id").GetString() ?? "" : "";
            var parts = comps.Where(c => c.TryGetProperty("group_id", out var gid) && gid.ValueKind == JsonValueKind.String && gid.GetString() == groupId)
                             .OrderBy(c => c.TryGetProperty("position", out var pos) ? pos.GetInt32() : 0)
                             .Select(c => new StatusPart(c.GetProperty("name").GetString() ?? "", Pretty(c.GetProperty("status").GetString())))
                             .ToList();
            string overall = group.ValueKind == JsonValueKind.Object ? Pretty(group.GetProperty("status").GetString()) : "Unknown";

            var notices = new List<StatusNotice>();
            foreach (var key in new[] { "incidents", "scheduled_maintenances" })
            {
                if (!root.TryGetProperty(key, out var arr)) continue;
                foreach (var i in arr.EnumerateArray())
                {
                    string name = i.GetProperty("name").GetString() ?? "";
                    bool fortnite = name.Contains("Fortnite", StringComparison.OrdinalIgnoreCase)
                        || (i.TryGetProperty("components", out var ic) && ic.EnumerateArray()
                               .Any(c => c.TryGetProperty("group_id", out var gid) && gid.ValueKind == JsonValueKind.String && gid.GetString() == groupId));
                    if (!fortnite) continue;
                    string detail = Pretty(i.GetProperty("status").GetString());
                    if (i.TryGetProperty("scheduled_for", out var sf) && sf.ValueKind == JsonValueKind.String && DateTime.TryParse(sf.GetString(), out var from))
                        detail = $"{from.ToLocalTime():ddd d MMM, h:mm tt}" + (i.TryGetProperty("scheduled_until", out var su) && su.ValueKind == JsonValueKind.String && DateTime.TryParse(su.GetString(), out var to) ? $" to {to.ToLocalTime():h:mm tt}" : "") + " · " + detail;
                    notices.Add(new StatusNotice(name, detail));
                }
            }
            return new FortniteStatus(overall, parts, notices, nowUtc);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }

    static string Pretty(string? s) => s switch
    {
        "operational" => "Working",
        "degraded_performance" => "Slow",
        "partial_outage" => "Partly down",
        "major_outage" => "Down",
        "under_maintenance" => "Maintenance",
        null or "" => "Unknown",
        _ => char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' '),
    };
}
