using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Diagnostics;

// Acceptance checks for the documented NuGet consumer logging contract.
internal static class DiagnosticsFlowChecks
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        foreach (var level in Enum.GetValues<LogLevel>())
            foreach (bool sql in new[] { false, true })
                foreach (bool values in new[] { false, true })
                    await test($"diagnostic flow: {level}, SQL={sql}, values={values}", async () =>
                    {
                        using var fixture = new Fixture("flow-private");
                        using var sink = new FlowLogProvider();
                        using var factory = LoggerFactory.Create(b => b.ClearProviders().SetMinimumLevel(level).AddProvider(sink));
                        var db = Database(fixture, factory.CreateLogger("SqlLinqer"), new() { IncludeSql = sql, IncludeParameterValues = values });
                        (await db.BeginSelect<User>().SelectRootColumns().Where(x => x.Name, "flow-private-1", SqlOp.EQ).Tag("flow.search").ExecuteAsync()).EnsureSuccess();
                        var failure = await db.ExecuteReaderListAsync<int>(new("SELECT MissingColumn FROM MissingTable") { QueryName = "flow.failure" });
                        Check(failure.State == ResponseState.Error, "Failure fixture unexpectedly succeeded");
                        bool debug = level <= LogLevel.Debug;
                        bool trace = level == LogLevel.Trace;
                        Check(sink.Entries.Count(e => e.Id.Id == 1000) == (debug ? 2 : 0), "QueryStarting filter mismatch");
                        Check(sink.Entries.Count(e => e.Id.Id == 1002) == (trace && sql ? 2 : 0), "SQL details ignored threshold/opt-in");
                        Check(sink.Entries.Count(e => e.Id.Id == 1003) == (trace ? 2 : 0), "Parameter details ignored threshold/opt-in");
                        Check(sink.Entries.Count(e => e.Id.Id == 1001 && e.Level == LogLevel.Debug) == (debug ? 1 : 0), "Success threshold mismatch");
                        Check(sink.Entries.Count(e => e.Level == LogLevel.Error) == (level <= LogLevel.Error ? 1 : 0), "Failure threshold mismatch");
                        Check(sink.Entries.All(e => e.Level is not (LogLevel.Information or LogLevel.Warning or LogLevel.Critical)), "Current observer unexpectedly emits another level");
                        Check(sink.Text.Contains("flow-private-1") == (trace && values), "Parameter default redaction changed");
                        Check(sink.Entries.All(e => e.Exception == null), "Exceptions leaked without their opt-in");
                    });

        await test("diagnostic flow: logging None does not disable sampled SQL activities", async () =>
        {
            using var fixture = new Fixture("trace-private"); using var tracing = new TraceCapture();
            using var sink = new FlowLogProvider();
            using var factory = LoggerFactory.Create(b => b.ClearProviders().SetMinimumLevel(LogLevel.None).AddProvider(sink));
            var db = Database(fixture, factory.CreateLogger("SqlLinqer"), new() { IncludeSqlInActivities = true, IncludeParameterValues = true });
            (await db.BeginSelect<User>().SelectRootColumns().Where(x => x.Name, "trace-private-1", SqlOp.EQ).ExecuteAsync()).EnsureSuccess();
            var activity = tracing.Stopped.Single();
            Check(sink.Entries.IsEmpty && activity.GetTagItem("db.query.text") is string text && text.Contains("SELECT"), "Logging and tracing controls became coupled");
            Check(!activity.TagObjects.Any(t => t.Value?.ToString()?.Contains("trace-private-1") == true), "Bindings became activity tags");
        });

        await test("diagnostic flow: no observer means no SqlLinqer logs or activities", async () =>
        {
            using var fixture = new Fixture("no-observer"); using var tracing = new TraceCapture();
            var db = new ConnectionFactoryConnector(fixture.Connect, DbFlavor.Sqlite);
            (await db.BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            Check(tracing.Started == 0, "Core enabled telemetry automatically");
        });

        await test("diagnostic flow: no logger/listener returns no observation even with detail options", async () =>
        {
            using var fixture = new Fixture("disabled");
            var spy = new StartSpy(new SqlLinqerDiagnostics(options: new() { IncludeSql = true, IncludeParameterValues = true }));
            var db = new ConnectionFactoryConnector(fixture.Connect, DbFlavor.Sqlite, new() { Observer = spy });
            (await db.BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            Check(spy.NoObservation, "Disabled instrumentation created an observation");
        });

        await test("diagnostic flow: cancellation and early disposal are Debug, not Error", async () =>
        {
            using var fixture = new Fixture("stream-flow"); using var sink = new FlowLogProvider();
            using var factory = LoggerFactory.Create(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(sink));
            var db = Database(fixture, factory.CreateLogger("SqlLinqer"), new());
            await foreach (var _ in db.BeginSelect<User>().SelectRootColumns().Tag("flow.early").ExecuteAsyncStream()) break;
            using var cancellation = new CancellationTokenSource();
            try
            {
                await foreach (var _ in db.BeginSelect<User>().SelectRootColumns().Tag("flow.cancel").ExecuteAsyncStream(cancellationToken: cancellation.Token)) cancellation.Cancel();
                throw new Exception("Cancellation fixture did not cancel");
            }
            catch (OperationCanceledException) { }
            var ended = sink.Entries.Where(e => e.Id.Id == 1001).ToArray();
            Check(ended.Length == 2 && ended.All(e => e.Level == LogLevel.Debug), "Cancellation/early disposal became errors");
            Check(ended.Any(e => Equals(e.State["Outcome"], QueryExecutionOutcome.StoppedEarly)) && ended.Any(e => Equals(e.State["Outcome"], QueryExecutionOutcome.Cancelled)), "Stream outcomes were lost");
        });

        await test("diagnostic flow: errors classify the provider, retain wrapper type and require explicit exception details", async () =>
        {
            using var fixture = new Fixture("error-flow"); using var sink = new FlowLogProvider();
            using var factory = LoggerFactory.Create(b => b.ClearProviders().SetMinimumLevel(LogLevel.Error).AddProvider(sink));
            foreach (bool detail in new[] { false, true })
            {
                var db = Database(fixture, factory.CreateLogger("SqlLinqer"), new() { IncludeExceptionDetails = detail });
                var failed = await db.ExecuteReaderListAsync<int>(new("SELECT MissingColumn FROM MissingTable"));
                var entry = sink.Entries.Last();
                Check(Equals(entry.State["ErrorType"], typeof(Microsoft.Data.Sqlite.SqliteException).FullName) && Equals(entry.State["WrapperType"], typeof(SqlLinqer.Exceptions.SqlResponseException).FullName) && Equals(entry.State["FailureKind"], "Database") && Equals(entry.State["ProviderErrorCode"], "1"), "Characterized error wrapper changed");
                Check(detail ? ReferenceEquals(entry.Exception, failed.Error) : entry.Exception == null, "Exception opt-in changed");
                Check(failed.Error.InnerException is Microsoft.Data.Sqlite.SqliteException, "Provider error was unavailable to the application");
            }
        });

        await test("diagnostic flow: typed parameter detail distinguishes null and empty strings", async () =>
        {
            using var fixture = new Fixture("binding-flow"); using var sink = new FlowLogProvider();
            using var factory = LoggerFactory.Create(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(sink));
            var db = Database(fixture, factory.CreateLogger("SqlLinqer"), new() { IncludeParameterValues = true });
            foreach (object? value in new object?[] { null, "" })
            {
                var query = new RenderedQuery("");
                string parameter = query.Parameters.AddParameter(value);
                query.Text = "SELECT CASE WHEN " + parameter + " IS NULL THEN 1 ELSE 2 END";
                (await db.ExecuteReaderListAsync<int>(query)).EnsureSuccess();
            }
            var bindings = sink.Entries.Where(e => e.Id.Id == 1003).Select(e => e.Text).ToArray();
            Check(bindings.Length == 2 && bindings[0] != bindings[1], "NULL and empty values became indistinguishable");
        });
    }

    private static ConnectionFactoryConnector Database(Fixture fixture, ILogger logger, SqlLinqerDiagnosticsOptions options)
        => new(fixture.Connect, DbFlavor.Sqlite, new() { Observer = new SqlLinqerDiagnostics(logger, options) });
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class StartSpy(IQueryObserver observer) : IQueryObserver
    {
        public bool NoObservation;
        public IQueryObservation? Start(QueryExecutionContext context)
        {
            var result = observer.Start(context); NoObservation = result == null; return result;
        }
    }
}

internal sealed record FlowLogEntry(LogLevel Level, EventId Id, string Category, string Text, Exception? Exception, IReadOnlyDictionary<string, object?> State);
internal sealed class FlowLogProvider : ILoggerProvider
{
    public ConcurrentQueue<FlowLogEntry> Entries { get; } = new();
    public string Text => string.Join("\n", Entries.Select(e => e.Text));
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);
    public void Dispose() { }
    private sealed class CapturingLogger(FlowLogProvider owner, string category) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => true; // LoggerFactory applies actual thresholds.
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
            => owner.Entries.Enqueue(new(level, id, category, formatter(state, error), error,
                state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToDictionary(x => x.Key, x => x.Value) : new Dictionary<string, object?>()));
    }
}
