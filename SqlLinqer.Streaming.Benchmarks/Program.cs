using System.Data.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

if (args.Length == 1 && args[0] == "mapping") return MappingBenchmarks.Run();

// Setup runs in a different process from every measurement. The Python runner
// interleaves modes and preserves every raw sample, including first-row latency.
if (args.Length < 4 || args[0] is not ("setup" or "measure"))
{
    Console.Error.WriteLine("setup <sqlite|duckdb> <file> <rows> [width]\nmeasure <sqlite|duckdb|duckdb-scan|sqlite-document> <file> <rows> <buffered|stream|batch|collect|first> [batch-size]");
    return 2;
}
string operation = args[0], provider = args[1], file = Path.GetFullPath(args[2]);
if (provider is not ("sqlite" or "duckdb" or "duckdb-scan" or "sqlite-document")) throw new ArgumentException("Unknown provider");
int rowCount = int.Parse(args[3]);
if (rowCount < 1) throw new ArgumentOutOfRangeException(nameof(rowCount));
bool duck = provider.StartsWith("duckdb", StringComparison.Ordinal);
DbConnection Create() => duck ? new DuckDBConnection($"Data Source={file}")
    : new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
using var keeper = Create();
await keeper.OpenAsync();
if (duck)
{
    using var configure = keeper.CreateCommand();
    configure.CommandText = "SET threads=2; SET memory_limit='1GB'";
    configure.ExecuteNonQuery();
}
if (operation == "setup")
{
    int width = args.Length > 4 ? int.Parse(args[4]) : 256;
    if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
    using var command = keeper.CreateCommand();
    command.CommandText = "CREATE TABLE bench_rows (Id INTEGER PRIMARY KEY, Name VARCHAR NOT NULL, Note VARCHAR)";
    command.ExecuteNonQuery();
    command.CommandText = duck
        ? $"INSERT INTO bench_rows SELECT i, $payload, CASE WHEN i % 10 = 0 THEN NULL ELSE '雪😀' END FROM range(1,{rowCount + 1}) t(i)"
        : $"WITH RECURSIVE source(i) AS (VALUES(1) UNION ALL SELECT i+1 FROM source WHERE i < {rowCount}) INSERT INTO bench_rows SELECT i, $payload, CASE WHEN i % 10 = 0 THEN NULL ELSE '雪😀' END FROM source";
    var payload = command.CreateParameter(); payload.ParameterName = "payload"; payload.Value = new string('x', width); command.Parameters.Add(payload);
    command.ExecuteNonQuery();
    Console.WriteLine(JsonSerializer.Serialize(new { provider, rowCount, width, fileBytes = new FileInfo(file).Length }));
    return 0;
}

string mode = args.Length > 4 ? args[4] : "stream";
if (mode is not ("buffered" or "stream" or "batch" or "collect" or "first")) throw new ArgumentException("Unknown mode");
int batchSize = args.Length > 5 ? int.Parse(args[5]) : 1000;
if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
var connector = new BenchmarkConnector(Create, duck ? DbFlavor.DuckDb : DbFlavor.Sqlite, provider == "sqlite-document");
SelectQuery<BenchRow> Query(int count)
{
    var query = new SelectQuery<BenchRow>().SelectRootColumns().Top(count);
    if (provider != "duckdb-scan") query.OrderBy(x => x.Id, SqlDir.ASC);
    return query;
}

RenderedQuery Document(int count) => new($"SELECT CASE WHEN Id=1 THEN '[' ELSE ',' END || json_object('Id',Id,'Name',Name,'Note',Note) || CASE WHEN Id={count} THEN ']' ELSE '' END FROM bench_rows WHERE Id <= {count} ORDER BY Id");
IAsyncEnumerable<BenchRow> ExecuteAsyncStream(int count) => provider == "sqlite-document"
    ? connector.ExecuteReaderJsonAsyncStream<BenchRow>(Document(count))
    : Query(count).ExecuteAsyncStream(connector);
async Task<List<BenchRow>> ExecuteAsync(int count)
{
    var response = provider == "sqlite-document"
        ? await connector.ExecuteReaderJsonAsync<BenchRow>(Document(count))
        : await Query(count).ExecuteAsync(connector);
    if (response.State == ResponseState.Error) throw response.Error;
    return response.Result;
}

// Warm both paths equally so serializer setup and provider initialization are
// excluded. The small fixture stays bounded even for a million-row measurement.
await ExecuteAsync(Math.Min(rowCount, 128));
await foreach (var row in ExecuteAsyncStream(Math.Min(rowCount, 128))) { }
using var versionCommand = keeper.CreateCommand();
versionCommand.CommandText = duck ? "SELECT version()" : "SELECT sqlite_version()";
string engineVersion = (string)versionCommand.ExecuteScalar();
using var widthCommand = keeper.CreateCommand(); widthCommand.CommandText = "SELECT length(Name) FROM bench_rows WHERE Id=1";
long actualWidth = Convert.ToInt64(widthCommand.ExecuteScalar());

GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
long baselineManaged = GC.GetTotalMemory(false);
long peakManaged = baselineManaged;
using var process = Process.GetCurrentProcess(); process.Refresh();
long baselineWorkingSet = process.WorkingSet64;
long allocatedStart = GC.GetTotalAllocatedBytes(true);
int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
using var sampler = new Timer(_ =>
{
    long value = GC.GetTotalMemory(false), current;
    do { current = Interlocked.Read(ref peakManaged); if (value <= current) break; }
    while (Interlocked.CompareExchange(ref peakManaged, value, current) != current);
}, null, 0, 5);
var clock = Stopwatch.StartNew();
double firstRowMs = -1;
long consumed = 0, checksum = 0, characters = 0;
List<BenchRow> retained = null;
void Consume(BenchRow row)
{
    consumed++; checksum += row.Id; characters += row.Name.Length + (row.Note?.Length ?? 0);
}
if (mode == "buffered")
{
    retained = await ExecuteAsync(rowCount);
    firstRowMs = clock.Elapsed.TotalMilliseconds;
    foreach (var row in retained) Consume(row);
}
else if (mode == "collect")
{
    retained = new List<BenchRow>();
    await foreach (var row in ExecuteAsyncStream(rowCount))
    {
        if (firstRowMs < 0) firstRowMs = clock.Elapsed.TotalMilliseconds;
        retained.Add(row);
    }
    foreach (var row in retained) Consume(row);
}
else if (mode == "batch")
{
    var batch = new List<BenchRow>(batchSize);
    await foreach (var row in ExecuteAsyncStream(rowCount))
    {
        if (firstRowMs < 0) firstRowMs = clock.Elapsed.TotalMilliseconds;
        batch.Add(row);
        if (batch.Count == batchSize) { foreach (var item in batch) Consume(item); batch.Clear(); }
    }
    foreach (var item in batch) Consume(item);
}
else
{
    await foreach (var row in ExecuteAsyncStream(rowCount))
    {
        if (firstRowMs < 0) firstRowMs = clock.Elapsed.TotalMilliseconds;
        Consume(row);
        if (mode == "first") break;
    }
}
clock.Stop();
long allocated = GC.GetTotalAllocatedBytes(true) - allocatedStart;
int[] collectionDelta = [GC.CollectionCount(0) - collections[0], GC.CollectionCount(1) - collections[1], GC.CollectionCount(2) - collections[2]];
long expectedCount = mode == "first" ? 1 : rowCount;
long expectedChecksum = mode == "first" && provider == "duckdb-scan" ? checksum : mode == "first" ? 1 : (long)rowCount * (rowCount + 1) / 2;
if (mode == "first" && (checksum < 1 || checksum > rowCount)) throw new Exception("First row ID is outside the fixture");
long expectedCharacters = mode == "first"
    ? actualWidth + (checksum % 10 == 0 ? 0 : "雪😀".Length)
    : actualWidth * expectedCount + (expectedCount - expectedCount / 10) * "雪😀".Length;
if (consumed != expectedCount || checksum != expectedChecksum || characters != expectedCharacters)
    throw new Exception($"Incorrect results: count={consumed}, checksum={checksum}, characters={characters}");
Interlocked.Exchange(ref peakManaged, Math.Max(Interlocked.Read(ref peakManaged), GC.GetTotalMemory(false)));
// Retained bytes describe live results after collection; this GC is outside timing.
sampler.Change(Timeout.Infinite, Timeout.Infinite);
long retainedManaged = GC.GetTotalMemory(true) - baselineManaged;
process.Refresh();
var measurement = new
{
    provider,
    mode,
    rows = rowCount,
    rowsConsumed = consumed,
    width = actualWidth,
    batchSize = mode == "batch" ? batchSize : 0,
    elapsedMs = clock.Elapsed.TotalMilliseconds,
    firstRowMs,
    rowsPerSecond = consumed / clock.Elapsed.TotalSeconds,
    allocatedBytes = allocated,
    allocatedBytesPerRow = (double)allocated / consumed,
    baselineManagedBytes = baselineManaged,
    peakManagedBytes = peakManaged,
    retainedManagedDeltaBytes = retainedManaged,
    baselineWorkingSetBytes = baselineWorkingSet,
    peakWorkingSetBytes = process.PeakWorkingSet64,
    collectionDelta,
    checksum,
    characters,
    engineVersion,
    providerVersion = keeper.GetType().Assembly.GetName().Version.ToString(),
    dotnet = Environment.Version.ToString(),
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    configuration = typeof(BenchRow).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
};
Console.WriteLine(JsonSerializer.Serialize(measurement));
GC.KeepAlive(retained);
return 0;

[SqlTable("bench_rows")]
internal sealed class BenchRow
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
    public string Note { get; set; }
}

internal sealed class BenchmarkConnector(Func<DbConnection> factory, DbFlavor flavor, bool document)
    : BaseConnector(flavor, new ConnectorOptions { CommandTimeout = TimeSpan.FromMinutes(5) })
{
    public override DbConnection CreateConnection() => factory();
    public override bool JsonResultsRowByRow() => !document;
}
