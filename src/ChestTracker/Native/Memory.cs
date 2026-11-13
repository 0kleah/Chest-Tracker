using System.Runtime;
using System.Runtime.InteropServices;

namespace ChestTracker.Native;

/// <summary>
/// Keeps the app's memory use low. Reading a replay needs a lot of memory for a second or two; .NET would keep
/// that memory reserved afterwards, so it's handed back to Windows once the work is done, and again whenever
/// the window is minimised or hidden.
/// </summary>
public static class Memory
{
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr process);

    public static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(GetCurrentProcess());
        }
        catch (Exception) { }
    }
}
