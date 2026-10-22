using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SqlLinqer.Comparisons;

try
{
    if (args.Length == 4 && args[0] == "setup")
    {
        int rows = Nonnegative(args[2]), width = Nonnegative(args[3]);
        if (rows > int.MaxValue / 10 - 1) throw new ArgumentOutOfRangeException(nameof(rows));
        Fixture.Create(Path.GetFullPath(args[1]), rows, width);
        Console.WriteLine(JsonSerializer.Serialize(new { rows, width }));
        return 0;
    }
    if (args.Length != 9 || args[0] != "measure")
        throw new ArgumentException("Usage: setup <new-file> <rows> <width>\nmeasure <library> <flat|graph|page|stream> <file> <page-size> <offset> <warmup> <operations> <expected-rows>");

    string library = args[1], workloadName = args[2], connectionString = Fixture.ConnectionString(args[3]);
    if (workloadName is not ("flat" or "graph" or "page" or "stream")) throw new ArgumentException("Unknown workload");
    int pageSize = Positive(args[4]), offset = Nonnegative(args[5]), warmup = Positive(args[6]), operations = Positive(args[7]);
    var info = Fixture.ReadInfo(connectionString);
    if (info.Rows != Nonnegative(args[8])) throw new InvalidDataException("Fixture row count differs from the requested run");
    var workload = new Workload(workloadName, info.Rows, pageSize, offset);
    var expected = Fixture.Expected(workload, info.Width);
    long? expectedTotal = workload.Page ? info.Rows - info.Rows / 3 : null;
    var log = new CommandLog();
    var competitor = Competitors.Create(library, connectionString, log);

    // Capture actual executed SQL and validate a full run before measuring.
    log.Reset(capture: true);
    Verify(await Consume(competitor, workload), expected, expectedTotal);
    object[] commands = log.Commands.ToArray();
    int commandsPerOperation = log.Count;
    if (commandsPerOperation < 1) throw new InvalidOperationException("Command instrumentation did not observe any queries");
    log.Reset();
    for (int i = 0; i < warmup; i++) Verify(await Consume(competitor, workload), expected, expectedTotal);
    log.Reset();

    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var results = new Consumption[operations];
    int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
    long allocatedStart = GC.GetTotalAllocatedBytes(precise: true);
    long started = Stopwatch.GetTimestamp();
    for (int i = 0; i < operations; i++) results[i] = await Consume(competitor, workload);
    double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedStart;
    int[] collections = [GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2];
    // Assertions and fingerprint formatting occur after timing and allocation capture.
    foreach (var result in results) Verify(result, expected, expectedTotal);
    if (log.Count != commandsPerOperation * operations) throw new InvalidDataException("Measured command count differs from the captured execution");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        library, workload = workloadName, fixtureRows = info.Rows, width = info.Width, pageSize, offset, warmup, operations,
        resultRows = expected.Rows, orders = expected.Orders, labels = expected.Labels, fingerprint = expected.Fingerprint, total = expectedTotal,
        elapsedMs, elapsedMsPerOperation = elapsedMs / operations,
        firstRowMs = expected.Rows == 0 ? (double?)null : results.Average(x => x.FirstRowMs),
        allocatedBytes = allocated, allocatedBytesPerOperation = (double)allocated / operations,
        commandsPerOperation, gcCollections = collections, commands,
        runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(), cpuCount = Environment.ProcessorCount,
        sqliteVersion = info.Engine,
        assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic)
            .Select(a => new { name = a.GetName().Name, version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion })
            .Where(a => a.name is "SQLLinqer" or "SqlLinqer" or "Dapper" or "SqlKata" or "SqlKata.Execution" or "linq2db" or "Microsoft.EntityFrameworkCore" or "Microsoft.Data.Sqlite")
            .OrderBy(a => a.name).ToArray()
    }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static int Nonnegative(string text) => int.TryParse(text, out int value) && value >= 0 ? value : throw new ArgumentException("Expected a nonnegative integer");
static int Positive(string text) => int.TryParse(text, out int value) && value > 0 ? value : throw new ArgumentException("Expected a positive integer");

static async Task<Consumption> Consume(ICompetitor competitor, Workload workload)
{
    long started = Stopwatch.GetTimestamp();
    double firstRow = 0;
    var digest = new Digest();
    long? total = null;
    if (workload.Stream)
    {
        await foreach (var row in competitor.Stream(workload))
        {
            if (digest.Rows == 0) firstRow = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            digest.Add(row, false);
        }
    }
    else
    {
        var result = await competitor.Load(workload);
        total = result.Total;
        if (result.Rows.Count > 0) firstRow = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        foreach (var row in result.Rows) digest.Add(row, workload.Graph);
    }
    return new Consumption(digest, total, firstRow);
}

static void Verify(Consumption result, Digest expected, long? expectedTotal)
{
    result.Digest.Verify(expected);
    if (result.Total != expectedTotal) throw new InvalidDataException($"Total mismatch: {result.Total}/{expectedTotal}");
}

internal sealed record Consumption(Digest Digest, long? Total, double FirstRowMs);
