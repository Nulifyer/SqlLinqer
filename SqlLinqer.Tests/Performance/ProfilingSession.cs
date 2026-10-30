using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;

namespace SqlLinqer.Tests.Performance;

// Benchmark-only attachment gate. The worker warms before announcing READY and
// starts work only after the controller has attached the profiler.
public static class ProfilingSession
{
    [EventSource(Name = "SqlLinqer-Profiling")]
    private sealed class Events : EventSource
    {
        internal static readonly Events Log = new();
        [Event(1, Opcode = EventOpcode.Info)] public void ProfileComplete() => WriteEvent(1);
    }

    public static async Task Run(string layer, int seconds, Func<Task<long>> operation)
    {
        if (seconds is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        var warmup = Stopwatch.StartNew();
        do { await operation(); } while (warmup.Elapsed.TotalSeconds < 2);
        _ = Events.Log;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Console.WriteLine(JsonSerializer.Serialize(new { ready = true, pid = Environment.ProcessId, layer }));
        Console.Out.Flush();
        if (Console.ReadLine() != "go") throw new InvalidOperationException("Expected profiler attachment gate");
        var clock = Stopwatch.StartNew(); long operations = 0, checksum = 0;
        do { checksum += await operation(); operations++; } while (clock.Elapsed.TotalSeconds < seconds);
        clock.Stop();
        Events.Log.ProfileComplete();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            layer,
            operations,
            checksum,
            elapsedMs = clock.Elapsed.TotalMilliseconds,
            runtime = Environment.Version.ToString(),
            note = "Profiled workload; not an uninstrumented benchmark"
        }));
        Console.Out.Flush();
        // Keep the runtime alive while dotnet-trace stops and collects rundown.
        if (Console.ReadLine() != "done") throw new InvalidOperationException("Expected profiler completion gate");
    }
}
