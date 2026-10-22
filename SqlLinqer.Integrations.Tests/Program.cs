using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Diagnostics;
using SqlLinqer.Modeling;

int passed = 0, failed = 0;
async Task Test(string name, Func<Task> run)
{
    try { await run(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

await Test("DI retains independent singleton default and keyed connectors with lazy factories", async () =>
{
    using var first = new Fixture("first"); using var second = new Fixture("second");
    int firstCalls = 0, secondCalls = 0, configured = 0;
    var services = new ServiceCollection();
    services.AddSqlLinqer(() => { firstCalls++; return first.Connect(); }, DbFlavor.Sqlite, options =>
    { configured++; options.CommandTimeout = TimeSpan.FromSeconds(12); });
    services.AddKeyedSqlLinqer("other", _ => { secondCalls++; return second.Connect(); }, DbFlavor.Sqlite);
    using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    var db = provider.GetRequiredService<BaseConnector>();
    var other = provider.GetRequiredKeyedService<BaseConnector>("other");
    Check(ReferenceEquals(db, provider.GetRequiredService<BaseConnector>()) && !ReferenceEquals(db, other), "Wrong connector lifetime");
    Check(firstCalls == 0 && secondCalls == 0 && configured == 1 && db.Options.CommandTimeout.TotalSeconds == 12, "Registration opened a connection or repeated configuration");
    var rows = (await db.BeginSelect<User>().SelectRootColumns().ExecuteAsync()).GetResultOrThrow();
    var otherRows = other.BeginSelect<User>().SelectRootColumns().Execute().GetResultOrThrow();
    Check(rows[0].Name == "first-1" && otherRows[0].Name == "second-1", "Keyed database selection changed");
    Check(firstCalls == 1 && secondCalls == 1, "Factory calls changed");
    try { services.AddSqlLinqer(first.Connect, DbFlavor.Sqlite); throw new Exception("Duplicate default accepted"); }
    catch (InvalidOperationException) { }
    try { services.AddKeyedSqlLinqer("other", first.Connect, DbFlavor.Sqlite); throw new Exception("Duplicate key accepted"); }
    catch (InvalidOperationException) { }
});

await Test("connector write factories preserve affected keys and observer row semantics", async () =>
{
    using var fixture = new Fixture("writes"); using var tracing = new TraceCapture();
    var db = new ConnectionFactoryConnector(fixture.Connect, DbFlavor.Sqlite,
        new ConnectorOptions { Observer = new SqlLinqerDiagnostics() });
    var inserted = (await db.BeginInsert<User, int>().InsertAuto(new User { Name = "inserted" }).ExecuteAsync()).EnsureSuccess();
    int id = inserted.TouchedPrimaryKeys.Single();
    Check(inserted.Result == 1 && id == 4, "Generated key metadata changed");
    var updated = (await db.BeginUpdate<User, int>().Update(x => x.Name, "updated").Where(x => x.Id, id, SqlOp.EQ).ExecuteAsync()).EnsureSuccess();
    Check(updated.Result == 1 && updated.TouchedPrimaryKeys.Single() == id, "Update key metadata changed");
    Check(db.BeginSelect<User>().SelectRootColumns().Where(x => x.Id, id, SqlOp.EQ).Execute().GetResultOrThrow().Single().Name == "updated", "Write factory targeted the wrong connector");
    var deleted = (await db.BeginDelete<User, int>().Where(x => x.Id, id, SqlOp.EQ).ExecuteAsync()).EnsureSuccess();
    Check(deleted.Result == 1 && deleted.TouchedPrimaryKeys.Single() == id, "Delete key metadata changed");
    var zero = (await db.BeginDelete<User>().Where(x => x.Id, id, SqlOp.EQ).ExecuteAsync()).GetResultOrThrow();
    Check(zero == 0 && tracing.Stopped.Last().GetTagItem("db.response.returned_rows") == null,
        "Zero affected rows were rejected or reported as returned rows");
});

await Test("DI uses application-owned singleton dependencies without disposing their connections early", async () =>
{
    using var fixture = new Fixture("source");
    var services = new ServiceCollection(); services.AddSingleton(fixture);
    services.AddSqlLinqer(sp => sp.GetRequiredService<Fixture>().Connect(), DbFlavor.Sqlite);
    using (var provider = services.BuildServiceProvider())
        Check((await provider.GetRequiredService<BaseConnector>().BeginSelect<User>().SelectRootColumns().ExecuteAsync()).GetResultOrThrow().Count == 3, "Injected factory failed");
    using var connection = fixture.Connect(); connection.Open();
    Check(connection.State == System.Data.ConnectionState.Open, "Registration disposed the application-owned fixture");
});

await Test("buffered diagnostics retain rows and failures and redact values by default", async () =>
{
    using var fixture = new Fixture("secret-token");
    using var tracing = new TraceCapture(); var logger = new MemoryLogger();
    var db = new ConnectionFactoryConnector(fixture.Connect, DbFlavor.Sqlite,
        new ConnectorOptions { Observer = new SqlLinqerDiagnostics(logger) });
    using var parent = new Activity("request").Start();
    var page = (await db.BeginSelect<User>().SelectRootColumns().Tag("users.page").OrderBy(x => x.Id, SqlDir.ASC).Page(1, 2)
        .ExecuteWithTotalCountAsync()).EnsureSuccess();
    Check(page.Result.Count == 2 && page.TotalResults == 3, "Response helper lost page metadata");
    var activities = tracing.Stopped.ToArray();
    Check(activities.Length == 2 && activities.All(a => a.DisplayName == "users.page" && a.ParentId == parent.Id), "Page/count activities or request parent missing");
    Check(activities.All(a => a.Status == ActivityStatusCode.Ok && a.GetTagItem("sqllinqer.command.duration_ms") != null), "Buffered completion or timing missing");
    Check(activities.All(a => a.GetTagItem("db.query.text") == null) && !logger.Text.Contains("secret-token"), "Default instrumentation exposed query values");
    var result = await db.BeginSelect<User>().SelectRootColumns().Where(x => x.Name, "secret-token-1", SqlOp.EQ).Tag("users.filter").ExecuteAsync();
    Check(result.GetResultOrThrow().Count == 1 && !logger.Text.Contains("secret-token"), "Filtering or parameter redaction failed");
    var error = await db.ExecuteReaderListAsync<User>(new RenderedQuery("SELECT unknown FROM missing_table") { QueryName = "users.fail" });
    Check(error.State == ResponseState.Error && tracing.Stopped.Last().Status == ActivityStatusCode.Error, "Failure did not produce an error activity");
    Check(logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception == null), "Default exception detail policy changed");
    var bytes = (await db.ExecuteScalarAsync<byte[]>(new RenderedQuery("SELECT X'0102'"))).GetResultOrThrow();
    Check(bytes.SequenceEqual(new byte[] { 1, 2 }) && tracing.Stopped.Last().GetTagItem("db.response.returned_rows") == null,
        "A scalar binary value was counted as returned rows");
});

await Test("SQL and parameter details require explicit logging options", async () =>
{
    using var fixture = new Fixture("private-value"); using var tracing = new TraceCapture(); var logger = new MemoryLogger();
    var db = new ConnectionFactoryConnector(fixture.Connect, DbFlavor.Sqlite, new ConnectorOptions
    { Observer = new SqlLinqerDiagnostics(logger, new SqlLinqerDiagnosticsOptions { IncludeSql = true, IncludeParameterValues = true }) });
    (await db.BeginSelect<User>().SelectRootColumns().Where(x => x.Name, "private-value-1", SqlOp.EQ).ExecuteAsync()).EnsureSuccess();
    Check(logger.Text.Contains("private-value-1") && logger.Text.Contains("SELECT"), "Opt-in SQL detail missing");
    Check(tracing.Stopped.Single().GetTagItem("db.query.text") is string sql && !sql.Contains("private-value-1"), "Parameter values leaked into activity SQL");
});

await Test("stream activities are deferred, include processing time, and stop after early disposal", async () =>
{
    using var fixture = new Fixture("stream"); using var tracing = new TraceCapture();
    var db = new ConnectionFactoryConnector(fixture.Connect, DbFlavor.Sqlite,
        new ConnectorOptions { Observer = new SqlLinqerDiagnostics() });
    var stream = db.BeginSelect<User>().SelectRootColumns().Tag("users.stream").ExecuteAsyncStream();
    Check(tracing.Started == 0, "Stream observed before enumeration");
    await foreach (var row in stream)
    {
        Check(tracing.Stopped.IsEmpty, "Activity ended before consumer processing");
        await Task.Delay(30); break;
    }
    var activity = tracing.Stopped.Single();
    Check((string?)activity.GetTagItem("sqllinqer.outcome") == "StoppedEarly" && (long?)activity.GetTagItem("db.response.returned_rows") == 1,
        "Early stream outcome or count wrong");
    Check((double)activity.GetTagItem("sqllinqer.duration_ms")! - (double)activity.GetTagItem("sqllinqer.command.duration_ms")! >= 20,
        "Consumer time was not separated from ExecuteReader time");
    await foreach (var row in stream) { }
    Check(tracing.Stopped.Count == 2 && (string?)tracing.Stopped.Last().GetTagItem("sqllinqer.outcome") == "Completed", "Re-enumeration did not complete a new activity");
});

await Test("stream cancellation and consumer errors retain their original behavior", async () =>
{
    using var fixture = new Fixture("cancel"); using var tracing = new TraceCapture();
    var db = new ConnectionFactoryConnector(fixture.Connect, DbFlavor.Sqlite, new ConnectorOptions { Observer = new SqlLinqerDiagnostics() });
    using var source = new CancellationTokenSource();
    try
    {
        await foreach (var row in db.BeginSelect<User>().SelectRootColumns().ExecuteAsyncStream(cancellationToken: source.Token)) source.Cancel();
        throw new Exception("Cancellation was ignored");
    }
    catch (OperationCanceledException error) { Check(error.CancellationToken == source.Token, "Cancellation token changed"); }
    Check((string?)tracing.Stopped.Last().GetTagItem("sqllinqer.outcome") == "Cancelled", "Cancellation outcome missing");
    var consumerError = new InvalidOperationException("consumer");
    try { await foreach (var row in db.BeginSelect<User>().SelectRootColumns().ExecuteAsyncStream()) throw consumerError; }
    catch (InvalidOperationException error) { Check(ReferenceEquals(error, consumerError), "Consumer exception was wrapped"); }
    Check((string?)tracing.Stopped.Last().GetTagItem("sqllinqer.outcome") == "StoppedEarly", "Consumer disposal misreported as a provider failure");
});

await Test("observation failures release activities without altering database results", async () =>
{
    using var fixture = new Fixture("logging"); using var tracing = new TraceCapture();
    foreach (bool debug in new[] { false, true })
    {
        var logger = new MemoryLogger { Throw = true, Debug = debug };
        var db = new ConnectionFactoryConnector(fixture.Connect, DbFlavor.Sqlite, new ConnectorOptions { Observer = new SqlLinqerDiagnostics(logger) });
        Check((await db.BeginSelect<User>().SelectRootColumns().ExecuteAsync()).GetResultOrThrow().Count == 3, "Logger failure changed results");
    }
    Check(tracing.Started == tracing.Stopped.Count, "Faulting logger leaked an activity");
});

await Test("observed execution respects caller transactions and concurrent request contexts", async () =>
{
    using var fixture = new Fixture("parallel"); using var tracing = new TraceCapture();
    int factories = 0;
    var db = new ConnectionFactoryConnector(() => { Interlocked.Increment(ref factories); return fixture.Connect(); }, DbFlavor.Sqlite,
        new ConnectorOptions { Observer = new SqlLinqerDiagnostics() });
    using (var caller = fixture.Connect())
    {
        caller.Open(); using var transaction = caller.BeginTransaction();
        (await db.BeginSelect<User>().SelectRootColumns().ExecuteAsync(transaction)).EnsureSuccess();
        Check(factories == 0 && caller.State == System.Data.ConnectionState.Open && transaction.Connection == caller, "Caller ownership changed");
        transaction.Rollback();
    }
    await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
    {
        using var parent = new Activity("request-" + index).Start();
        var name = "users.parallel." + index;
        (await db.BeginSelect<User>().SelectRootColumns().Tag(name).ExecuteAsync()).EnsureSuccess();
        Check(tracing.Stopped.Single(a => a.DisplayName == name).ParentId == parent.Id, "Concurrent activity parent crossed requests");
    })));
    Check(factories == 8, "Concurrent factory count changed");
});

Console.WriteLine($"{passed} integration checks passed, {failed} failed");
return failed == 0 ? 0 : 1;

[SqlTable("users")]
sealed class User
{
    [SqlPrimaryKey] public int Id { get; set; }
    [SqlColumn("display_name"), JsonPropertyName("apiName")] public string Name { get; set; } = "";
}
sealed class Fixture : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "sqllinqer-adoption-" + Guid.NewGuid().ToString("N") + ".db");
    public Fixture(string prefix)
    {
        using var connection = Connect(); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE users (Id INTEGER PRIMARY KEY, display_name TEXT NOT NULL)"; command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO users VALUES ($id, $name)"; command.Parameters.Add("$id", SqliteType.Integer); command.Parameters.Add("$name", SqliteType.Text);
        for (int id = 1; id <= 3; id++) { command.Parameters[0].Value = id; command.Parameters[1].Value = prefix + "-" + id; command.ExecuteNonQuery(); }
    }
    public SqliteConnection Connect() => new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
    public void Dispose() => File.Delete(path);
}
sealed class TraceCapture : IDisposable
{
    private readonly ActivityListener listener;
    public readonly ConcurrentQueue<Activity> Stopped = new();
    private int started;
    public int Started => started;
    public TraceCapture()
    {
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SqlLinqerDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => Interlocked.Increment(ref started),
            ActivityStopped = a => Stopped.Enqueue(a)
        };
        ActivitySource.AddActivityListener(listener);
    }
    public void Dispose() => listener.Dispose();
}
sealed class MemoryLogger : ILogger
{
    public readonly ConcurrentQueue<(LogLevel Level, string Text, Exception? Exception)> Entries = new();
    public bool Throw { get; init; }
    public bool Debug { get; init; } = true;
    public string Text => string.Join("\n", Entries.Select(x => x.Text));
    public bool IsEnabled(LogLevel level) => level != LogLevel.Debug || Debug;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    { if (Throw) throw new InvalidOperationException("logger"); Entries.Enqueue((level, formatter(state, exception), exception)); }
}
