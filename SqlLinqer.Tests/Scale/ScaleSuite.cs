using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Scale;

// Included by --all; local runs select --suite scale explicitly. No external endpoints are used.
internal static class ScaleSuite
{
    private const int Buckets = 97;
    private sealed record CheckResult(string Provider, string Name, bool Passed, string Error);
    private sealed record Measurement(string Provider, string Operation, int Rows, int Warmups,
        double[] Milliseconds, long[] ManagedBytes, double MedianMilliseconds, double RowsPerSecond);

    internal static int Run(int rows, int iterations, string resultsPath)
    {
        var checks = new List<CheckResult>();
        var measurements = new List<Measurement>();
        var providers = new Dictionary<string, string>();
        var started = DateTimeOffset.UtcNow;
        bool release = typeof(ScaleSuite).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration == "Release";
        Console.WriteLine($"SCALE rows={rows} iterations={iterations} configuration={(release ? "Release" : "Debug")} .NET={Environment.Version}");
        if (!release) Console.WriteLine("Use -c Release for performance comparisons.");

        void Test(string provider, string name, Action action)
        {
            try
            {
                action();
                checks.Add(new(provider, name, true, null));
                Console.WriteLine($"PASS {provider}: {name}");
            }
            catch (Exception error)
            {
                checks.Add(new(provider, name, false, error.ToString()));
                Console.Error.WriteLine($"FAIL {provider}: {name}\n{error}");
            }
        }

        // Validate every sample after stopping the clock. Failed validation fails the suite.
        void MeasureResult<T>(string provider, string name, int count, Func<T> action, Action<T> validate, bool warmup = true)
        {
            if (warmup) validate(action());
            var times = new double[warmup ? iterations : 1];
            var allocations = new long[times.Length];
            for (int i = 0; i < times.Length; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                T result = action();
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
                validate(result);
            }
            var sorted = times.Order().ToArray();
            double median = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
            measurements.Add(new(provider, name, count, warmup ? 1 : 0, times, allocations, median, count / (median / 1000)));
            Console.WriteLine(FormattableString.Invariant($"PERF {provider} {name} rows={count} samples={times.Length} median_ms={median:F3} rows_per_second={count / (median / 1000):F0} managed_bytes_median={allocations.Order().ElementAt(allocations.Length / 2)}"));
        }
        void Measure(string provider, string name, int count, Action action, bool warmup = true)
            => MeasureResult(provider, name, count, () => { action(); return 0; }, _ => { }, warmup);

        foreach (var flavor in new[] { DbFlavor.Sqlite, DbFlavor.DuckDb })
        {
            string provider = flavor.ToString();
            Test(provider, "fixture and large-data checks", () =>
            {
                using var database = new ScaleDatabase(flavor);
                var connector = database.Connector;
                providers[provider] = database.Version;
                Console.WriteLine($"{provider}: {database.Version}; parameter budget=999; DuckDB threads=2, memory_limit=512MB");
                database.Sql("CREATE TABLE scale_groups (Id INTEGER PRIMARY KEY, Name VARCHAR); " +
                    "CREATE TABLE scale_rows (Id INTEGER PRIMARY KEY, Bucket INTEGER NOT NULL, Name VARCHAR, Note VARCHAR, Score BIGINT, Payload BLOB); " +
                    "CREATE TABLE scale_writes (Id INTEGER PRIMARY KEY, Name VARCHAR NOT NULL)");
                for (int i = 0; i < Buckets; i++)
                    database.Sql($"INSERT INTO scale_groups VALUES ({i}, 'group-{i}')");

                Measure(provider, "batched-insert", rows, () =>
                {
                    // Bound client fixture memory while crossing hundreds of internal insert batches.
                    using var connection = connector.CreateConnection(); connection.Open();
                    using var transaction = connection.BeginTransaction();
                    for (int offset = 0; offset < rows; offset += 2000)
                    {
                        var batch = Enumerable.Range(offset + 1, Math.Min(2000, rows - offset)).Select(Row).ToArray();
                        Check(Result(new InsertQuery<ScaleRow>().InsertAuto(batch).Execute(connector, transaction)) == batch.Length,
                            $"Insert lost rows at offset {offset}");
                    }
                    transaction.Commit();
                    Check(database.Count("scale_rows") == rows, "Insert count differs from fixture size");
                }, warmup: false);

                Test(provider, "full typed JSON preserves every key and payload", () =>
                    MeasureResult(provider, "typed-json-all", rows,
                        () => Result(new SelectQuery<ScaleRow>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector)), actual => {
                        Check(actual.Count == rows, "JSON row count changed");
                        for (int i = 0; i < actual.Count; i++) CheckRow(actual[i], i + 1);
                    }));

                Test(provider, "ExecuteAsync full typed JSON preserves every key and payload", () =>
                    MeasureResult(provider, "typed-json-all-async", rows,
                        () => Result(new SelectQuery<ScaleRow>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC)
                            .ExecuteAsync(connector).GetAwaiter().GetResult()), actual => {
                        Check(actual.Count == rows, "Async JSON row count changed");
                        for (int i = 0; i < actual.Count; i++) CheckRow(actual[i], i + 1);
                    }));

                Test(provider, "ExecuteAsyncStream validates every row without retaining the result set", () =>
                {
                    async Task<(int Count, long Sum)> Consume()
                    {
                        int count = 0; long sum = 0;
                        await foreach (var row in new SelectQuery<ScaleRow>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC)
                            .ExecuteAsyncStream(connector))
                        {
                            CheckRow(row, ++count);
                            sum += row.Score;
                        }
                        return (count, sum);
                    }
                    // Streaming checks rows while consuming them to keep retention bounded.
                    MeasureResult(provider, "typed-json-all-async-stream", rows, () => Consume().GetAwaiter().GetResult(), actual =>
                        Check(actual.Count == rows && actual.Sum == ScoreSum(rows), "Streaming count or checksum changed"));
                });

                Test(provider, "flat DataTable preserves count and Int64 checksum", () =>
                    MeasureResult(provider, "datatable-all", rows,
                        () => Result(connector.ExecuteReaderDataTable(new RenderedQuery("SELECT Id, Score FROM scale_rows ORDER BY Id"))), table => {
                        using var actual = table;
                        Check(actual.Rows.Count == rows, "DataTable lost rows");
                        long sum = 0;
                        for (int i = 0; i < actual.Rows.Count; i++)
                        {
                            Check(Convert.ToInt32(actual.Rows[i][0]) == i + 1, "DataTable key order changed");
                            sum += Convert.ToInt64(actual.Rows[i][1]);
                        }
                        Check(sum == ScoreSum(rows), "DataTable Int64 checksum changed");
                    }));

                Test(provider, "deep and empty pages retain total and query state", () =>
                    Measure(provider, "deep-page-with-count", Math.Min(257, rows), () =>
                    {
                        int offset = rows - 257;
                        var query = new SelectQuery<ScaleRow>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Limit(257, offset);
                        string before = query.Render(connector).Text;
                        var page = query.ExecuteWithTotalCount(connector);
                        Check(Result(page).Select(x => x.Id).SequenceEqual(Enumerable.Range(offset + 1, 257)) && page.TotalResults == rows,
                            "Deep page keys or total changed");
                        Check(query.Render(connector).Text == before, "Count mutated paging");
                        var empty = new SelectQuery<ScaleRow>().Select(x => x.Id).OrderBy(x => x.Id, SqlDir.ASC)
                            .Limit(257, rows + 1).ExecuteWithTotalCount(connector);
                        Check(Result(empty).Count == 0 && empty.TotalResults == rows, "Empty page lost total");
                    }));

                Test(provider, "grouped aggregates match independently calculated counts and sums", () =>
                    MeasureResult(provider, "grouped-aggregate", rows,
                        () => Result(new SelectQueryInitializer<ScaleRow>().BuildQuery(q => new {
                            Bucket = q.Select(x => x.Bucket), Count = q.CountBig(), Sum = (long)q.Sum(x => x.Score)
                        }).Execute(connector)), actual => {
                        Check(actual.Count == Buckets, "Group count changed");
                        foreach (var group in actual)
                        {
                            int count = 0; long sum = 0;
                            for (int id = group.Bucket == 0 ? Buckets : group.Bucket; id <= rows; id += Buckets)
                            { count++; sum += (long)id * 1000003; }
                            Check(group.Count == count && group.Sum == sum, $"Aggregate changed for bucket {group.Bucket}");
                        }
                    }));

                Test(provider, "async nested paging preserves payloads and the full count", () =>
                {
                    async Task VerifyAsync()
                    {
                        var response = await new SelectQuery<ScaleRow>().SelectRootColumns().Select(x => x.Group)
                            .OrderBy(x => x.Id, SqlDir.ASC).Limit(128, rows - 128).ExecuteWithTotalCountAsync(connector);
                        var actual = Result(response);
                        Check(actual.Count == 128 && response.TotalResults == rows, "Async page count or total changed");
                        for (int i = 0; i < actual.Count; i++)
                        {
                            CheckRow(actual[i], rows - 127 + i);
                            Check(actual[i].Group.Id == actual[i].Bucket, "Async relationship crossed parent keys");
                        }
                    }
                    VerifyAsync().GetAwaiter().GetResult();
                });

                Test(provider, "ExecuteAsyncStream preserves nested page keys and binary payloads", () =>
                {
                    async Task VerifyAsync()
                    {
                        int count = 0;
                        await foreach (var row in new SelectQuery<ScaleRow>().SelectRootColumns().Select(x => x.Group)
                            .OrderBy(x => x.Id, SqlDir.ASC).Limit(128, rows - 128).ExecuteAsyncStream(connector))
                        {
                            CheckRow(row, rows - 127 + count++);
                            Check(row.Group.Id == row.Bucket, "Streamed relationship crossed parent keys");
                        }
                        Check(count == 128, "Streamed nested page count changed");
                    }
                    VerifyAsync().GetAwaiter().GetResult();
                });

                Test(provider, "large IN filter binds values once and returns exact keys", () =>
                {
                    int[] keys = Enumerable.Range(1, 900).Select(i => 1 + (i * 7919 % rows)).Distinct().ToArray();
                    var query = new SelectQuery<ScaleRow>().Select(x => x.Id).Where(x => x.Id, keys, SqlArrayOp.IN).OrderBy(x => x.Id, SqlDir.ASC);
                    Check(query.Render(connector).Parameters.Count == keys.Length, "IN parameter count changed");
                    Check(Result(query.Execute(connector)).Select(x => x.Id).SequenceEqual(keys.Order()), "IN values were lost");
                });

                Test(provider, "nested objects at scale retain each parent value", () =>
                    MeasureResult(provider, "nested-page", 512,
                        () => Result(new SelectQuery<ScaleRow>().SelectRootColumns().Select(x => x.Group)
                            .OrderBy(x => x.Id, SqlDir.ASC).Limit(512, rows - 512).Execute(connector)), actual => {
                        Check(actual.Count == 512, "Nested page lost rows");
                        foreach (var row in actual)
                            Check(row.Group.Id == row.Bucket && row.Group.Name == $"group-{row.Bucket}", "Related object crossed parent keys");
                    }));

                Test(provider, "large nested array preserves every correlated child", () =>
                    MeasureResult(provider, "nested-array", rows / Buckets,
                        () => Result(new SelectQuery<ScaleTree>().SelectRootColumns().Select(x => x.Members)
                            .Where(x => x.Id, 0, SqlOp.EQ).Execute(connector)), actual => {
                        var parent = actual.Single();
                        Check(parent.Id == 0 && parent.Name == "group-0", "Nested parent changed");
                        Check(parent.Members.Count == rows / Buckets, "Nested array lost children");
                        var members = parent.Members.OrderBy(x => x.Id).ToArray();
                        for (int i = 0; i < members.Length; i++)
                            Check(members[i].Id == (i + 1) * Buckets && members[i].Bucket == 0 &&
                                members[i].Score == (long)members[i].Id * 1000003, "Nested child crossed parent keys or changed value");
                    }));

                Test(provider, "large update and delete remain in caller transaction", () =>
                {
                    using var connection = connector.CreateConnection(); connection.Open();
                    using var transaction = connection.BeginTransaction();
                    Check(Result(new UpdateQuery<ScaleRow>().Update(x => x.Name, "changed").Where(x => x.Id, rows - 500, SqlOp.GT)
                        .Execute(connector, transaction)) == 500, "Update affected wrong rows");
                    Check(Result(new SelectQuery<ScaleRow>().Select(x => x.Id).Where(x => x.Name, "changed", SqlOp.EQ)
                        .Execute(connector, transaction)).Count == 500, "Update ignored transaction");
                    Check(Result(new DeleteQuery<ScaleRow>().Where(x => x.Id, rows - 500, SqlOp.GT).Execute(connector, transaction)) == 500,
                        "Delete affected wrong rows");
                    transaction.Rollback();
                    Check(database.Count("scale_rows") == rows, "Rollback lost rows");
                    CheckRow(Result(new SelectQuery<ScaleRow>().SelectRootColumns().Where(x => x.Id, rows, SqlOp.EQ).Execute(connector)).Single(), rows);
                    Check(connection.State == ConnectionState.Open, "Caller connection was disposed");
                });

                Test(provider, "late duplicate rolls back all owned batches and exposes no keys", () =>
                {
                    var data = Enumerable.Range(1, 1003).Select(i => new WriteRow { Id = i, Name = $"write-{i}" })
                        .Append(new WriteRow { Id = 1, Name = "late duplicate" }).ToArray();
                    var response = new InsertWithPrimaryKeyQuery<WriteRow, int>().InsertAuto(data).Execute(connector);
                    Check(response.State == ResponseState.Error && response.TouchedPrimaryKeys?.Any() != true, "Failed write exposed partial success");
                    Check(database.Count("scale_writes") == 0 && database.Count("scale_rows") == rows, "Late failure committed earlier batches");
                    var retry = new InsertWithPrimaryKeyQuery<WriteRow, int>().InsertAuto(data[..^1]).Execute(connector);
                    Check(Result(retry) == 1003 && retry.TouchedPrimaryKeys.Order().SequenceEqual(Enumerable.Range(1, 1003)), "Recovery lost keys");
                });

                Test(provider, "four concurrent readers preserve independent parameters", () =>
                {
                    Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = 4 }, worker =>
                    {
                        int offset = worker * 37 % (rows - 64);
                        var query = new SelectQuery<ScaleRow>().SelectRootColumns().Where(x => x.Id, offset, SqlOp.GT)
                            .OrderBy(x => x.Id, SqlDir.ASC).Top(64);
                        var actual = Result(query.Execute(connector));
                        Check(actual.Count == 64, "Concurrent read changed count");
                        for (int i = 0; i < actual.Count; i++) CheckRow(actual[i], offset + i + 1);
                    });
                });
            });
        }

        foreach (var flavor in Enum.GetValues<DbFlavor>())
            foreach (int predicates in new[] { 10, 100, 1000 })
                Test(flavor.ToString(), $"render {predicates} predicates", () =>
                {
                    var query = new SelectQuery<ScaleRow>().Select(x => x.Id);
                    for (int i = 0; i < predicates; i++) query.Where(x => x.Id, i, SqlOp.GT);
                    MeasureResult(flavor.ToString(), $"render-{predicates}", 1, () => query.Render(flavor), actual => {
                        Check(actual.Parameters.GetAll().Select(x => x.Value).SequenceEqual(Enumerable.Range(0, predicates).Cast<object>()),
                            "Rendered predicate bindings changed");
                    });
                });

        int failed = checks.Count(x => !x.Passed);
        Console.WriteLine($"Scale: {checks.Count - failed} passed, {failed} failed.");
        if (resultsPath != null)
        {
            string path = Path.GetFullPath(resultsPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(new {
                SchemaVersion = 1, StartedUtc = started, FinishedUtc = DateTimeOffset.UtcNow, Rows = rows, Iterations = iterations,
                Configuration = release ? "Release" : "Debug", Runtime = RuntimeInformation.FrameworkDescription,
                OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                LogicalProcessors = Environment.ProcessorCount, Providers = providers,
                PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                MeasurementNotes = "Wall time includes query construction, connection creation, execution and materialization. Buffered full JSON, DataTable, grouped aggregate, nested objects/arrays and rendering validate outside timing/allocation samples. Async-stream full JSON validates each row during consumption without retaining results. Insert includes fixture generation and per-batch assertions; deep-page includes empty-page/state assertions. Managed bytes cover only the calling thread, excluding native/provider worker allocations; async work can switch threads. Peak working set covers the entire process, not individual workloads. Insert is one cold sample; other operations have one warmup. DataTable projects two columns; typed JSON projects six. These are different workloads, not an overhead comparison. No timing thresholds.",
                Checks = checks, Measurements = measurements, Passed = failed == 0
            }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            Console.WriteLine($"Scale results: {path}");
        }
        return failed == 0 ? 0 : 1;
    }

    private static T Result<T>(SqlResponse<T> response) => response.State == ResponseState.Valid ? response.Result : throw response.Error;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static long ScoreSum(int rows) => (long)rows * (rows + 1) / 2 * 1000003;
    private static ScaleRow Row(int id) => new() {
        Id = id, Bucket = id % Buckets, Name = id % 13 == 0 ? null : $"row-{id}: 'quotes' \"json\" \\ %_\n雪🙂",
        Note = id % 7 == 0 ? null : id % 7 == 1 ? "" : $"note-{id}", Score = (long)id * 1000003,
        Payload = id % 11 == 0 ? null : id % 11 == 1 ? [] : Enumerable.Range(0, 32).Select(i => (byte)((id + i) % 256)).ToArray()
    };
    private static void CheckRow(ScaleRow actual, int id)
    {
        var expected = Row(id);
        Check(actual.Id == expected.Id && actual.Bucket == expected.Bucket && actual.Name == expected.Name &&
            actual.Note == expected.Note && actual.Score == expected.Score &&
            (expected.Payload == null ? actual.Payload == null : actual.Payload != null && actual.Payload.SequenceEqual(expected.Payload)),
            $"Roundtrip changed row {id}");
    }

    private sealed class ScaleDatabase : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"sqllinqer-scale-{Guid.NewGuid():N}");
        private DbConnection keeper;
        internal BaseConnector Connector { get; private set; }
        internal string Version { get; private set; }
        internal ScaleDatabase(DbFlavor flavor)
        {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "fixture.db");
            try
            {
                DbConnection Create() => flavor == DbFlavor.Sqlite
                    ? new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString())
                    : new DuckDBConnection($"Data Source={file}");
                keeper = Create(); keeper.Open();
                if (flavor == DbFlavor.DuckDb)
                {
                    using var command = keeper.CreateCommand();
                    command.CommandText = "SET threads=2; SET memory_limit='512MB'";
                    command.ExecuteNonQuery();
                }
                Connector = new ConnectionFactoryConnector(Create, flavor,
                    new ConnectorOptions { ParameterLimit = 999, CommandTimeout = TimeSpan.FromMinutes(2) });
                Version = $"{keeper.GetType().Assembly.GetName().Version}; engine=" + Result(Connector.ExecuteScalar<string>(
                    new RenderedQuery(flavor == DbFlavor.Sqlite ? "SELECT sqlite_version()" : "SELECT version()")));
            }
            catch { Dispose(); throw; }
        }
        internal void Sql(string text) => Result(Connector.ExecuteNonQuery(new RenderedQuery(text)));
        internal long Count(string table) => Result(Connector.ExecuteScalar<long>(new RenderedQuery($"SELECT COUNT(*) FROM {table}")));
        public void Dispose()
        {
            keeper?.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [SqlTable("scale_rows"), SqlAutoInsert]
    private sealed class ScaleRow
    {
        [SqlPrimaryKey(false)] public int Id { get; set; }
        public int Bucket { get; set; }
        public string Name { get; set; }
        public string Note { get; set; }
        public long Score { get; set; }
        public byte[] Payload { get; set; }
        [SqlOneToOne(nameof(Bucket))] public ScaleGroup Group { get; set; }
    }
    [SqlTable("scale_groups")]
    private sealed class ScaleGroup
    {
        [SqlPrimaryKey(false)] public int Id { get; set; }
        public string Name { get; set; }
    }
    [SqlTable("scale_writes"), SqlAutoInsert]
    private sealed class WriteRow
    {
        [SqlPrimaryKey(false)] public int Id { get; set; }
        public string Name { get; set; }
    }
    [SqlTable("scale_groups")]
    private sealed class ScaleTree
    {
        [SqlPrimaryKey(false)] public int Id { get; set; }
        public string Name { get; set; }
        [SqlOneToMany(nameof(ScaleMember.Bucket))] public List<ScaleMember> Members { get; set; }
    }
    [SqlTable("scale_rows")]
    private sealed class ScaleMember
    {
        [SqlPrimaryKey(false)] public int Id { get; set; }
        public int Bucket { get; set; }
        public long Score { get; set; }
    }
}
