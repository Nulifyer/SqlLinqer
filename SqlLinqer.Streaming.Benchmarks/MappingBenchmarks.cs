using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;

// Measures cached SQL model discovery separately from database execution.
internal static class MappingBenchmarks
{
    internal static int RunCache()
    {
        Func<int> defaultSchema = () => Table.GetCached<Row>().Columns.Count;
        Func<int> explicitSchema = () => Table.GetCached<Row>("benchmark_schema").Columns.Count;
        for (int i = 0; i < 2000; i++) { defaultSchema(); explicitSchema(); }
        for (int sample = 0; sample < 3; sample++)
        {
            Measure("table-cache-default-schema", 100000, defaultSchema, sample);
            Measure("table-cache-explicit-schema", 100000, explicitSchema, sample);
        }
        return 0;
    }

    private static void Measure(string name, int operations, Func<int> action, int sample)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        long checksum = 0;
        for (int i = 0; i < operations; i++) checksum += action();
        watch.Stop();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            name,
            sample,
            operations,
            checksum,
            allocatedBytesPerOperation = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / operations,
            nanosecondsPerOperation = watch.Elapsed.TotalNanoseconds / operations,
            runtime = Environment.Version.ToString()
        }));
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Note { get; set; }
    }
}
