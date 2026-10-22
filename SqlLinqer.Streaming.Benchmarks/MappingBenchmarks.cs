using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;

// This isolates serializer allocation from provider buffers and database execution.
// Reflection retrieves the actual library options without adding a public mapping interface.
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

    internal static int Run()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var sql = (JsonSerializerOptions)typeof(BaseConnector).GetField("JsonOptions", flags).GetValue(null);
        var sqlite = (JsonSerializerOptions)typeof(BaseConnector).GetField("SqliteJsonOptions", flags).GetValue(null);
        const string model = "{\"Id\":7,\"Name\":\"sample\",\"Note\":\"雪😀\"}";
        byte[] bytes = Enumerable.Range(0, 4096).Select(x => (byte)x).ToArray();
        string binary = JsonSerializer.Serialize(Convert.ToHexString(bytes));
        for (int i = 0; i < 2000; i++) JsonSerializer.Deserialize<Row>(model, sql);
        for (int i = 0; i < 100; i++) JsonSerializer.Deserialize<byte[]>(binary, sqlite);
        if (!JsonSerializer.Deserialize<byte[]>(binary, sqlite).SequenceEqual(bytes)) throw new Exception("Binary decoding differs");
        var mapped = JsonSerializer.Deserialize<Row>(model, sql);
        if (mapped.Id != 7 || mapped.Name != "sample" || mapped.Note != "雪😀") throw new Exception("Mapped row differs");
        for (int sample = 0; sample < 3; sample++)
        {
            Measure("sql-model-three-members", 100000, () => JsonSerializer.Deserialize<Row>(model, sql).Id, sample);
            Measure("sqlite-hex-4096-bytes", 1000, () => JsonSerializer.Deserialize<byte[]>(binary, sqlite).Length, sample);
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
