using System.IO;

namespace ChestTracker.Core;

/// <summary>
/// The start of a Fortnite .replay file: enough to know whether the match is over. While a match is
/// being played the replay is "live" and its decryption key is left blank; Fortnite fills the key in
/// when the match ends, and only then can the chests in it be read.
/// </summary>
public sealed record ReplayFileInfo(uint FileVersion, int LengthMs, bool IsLive, bool Encrypted, bool KeyPresent, DateTime? StartedUtc)
{
    const uint Magic = 0x1CA2E27F;

    /// <summary>The match is over and the replay can be read.</summary>
    public bool Complete => !IsLive && (!Encrypted || KeyPresent);

    public static ReplayFileInfo? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[Math.Min(fs.Length, 8192)];
            int n = fs.Read(buf, 0, buf.Length);
            return Parse(buf, n);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static ReplayFileInfo? Parse(byte[] d, int length)
    {
        int o = 0;
        bool Has(int bytes) => bytes >= 0 && o + bytes <= length;
        uint U32() { uint v = BitConverter.ToUInt32(d, o); o += 4; return v; }
        int I32() { int v = BitConverter.ToInt32(d, o); o += 4; return v; }

        if (!Has(8) || U32() != Magic) return null;
        uint version = U32();
        if (version >= 7)   // custom version table: count, then (16-byte guid + int32) each
        {
            if (!Has(4)) return null;
            int count = I32();
            if (count < 0 || count > 256 || !Has(count * 20)) return null;
            o += count * 20;
        }
        if (!Has(12)) return null;
        int lengthMs = I32();
        U32();          // network version
        U32();          // changelist
        if (!Has(4)) return null;
        int nameLen = I32();   // FString: negative = UTF-16
        long nameBytes = nameLen < 0 ? -2L * nameLen : nameLen;
        if (nameBytes < 0 || nameBytes > 4096 || !Has((int)nameBytes)) return null;
        o += (int)nameBytes;
        if (!Has(4)) return null;
        bool live = U32() != 0;
        DateTime? started = null;
        if (version >= 3)
        {
            if (!Has(8)) return null;
            long ticks = BitConverter.ToInt64(d, o);
            o += 8;
            if (ticks > DateTime.MinValue.Ticks && ticks < DateTime.MaxValue.Ticks) started = new DateTime(ticks, DateTimeKind.Utc);
        }
        if (version >= 2) { if (!Has(4)) return null; U32(); }   // compressed
        bool encrypted = false, key = false;
        if (version >= 6)
        {
            if (!Has(8)) return null;
            encrypted = U32() != 0;
            int keyLen = I32();
            if (keyLen < 0 || keyLen > 64 || !Has(keyLen)) return null;
            for (int i = 0; i < keyLen; i++) if (d[o + i] != 0) { key = true; break; }
            o += keyLen;
        }
        return new ReplayFileInfo(version, lengthMs, live, encrypted, key, started);
    }
}
