using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Diagnostics;

internal static class DiagnosticsPolicyChecks
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("diagnostics policy: separate SQL destinations and independent value opt-in", async () =>
        {
            using var fixture = new Fixture("signal-secret");
            foreach (bool logs in new[] { false, true }) foreach (bool activities in new[] { false, true })
            {
                using var tracing = new TraceCapture(); using var sink = new FlowLogProvider();
                using var factory = Factory(sink);
                var db = Db(fixture, new(factory.CreateLogger("SqlLinqer"), new() { IncludeSqlInLogs = logs, IncludeSqlInActivities = activities, IncludeParameterValues = true }));
                (await db.BeginSelect<User>().SelectRootColumns().Where(x => x.Name, "signal-secret-1", SqlOp.EQ).ExecuteAsync()).EnsureSuccess();
                Check(sink.Entries.Any(e => e.Id.Id == 1002) == logs, "SQL logging flag did not isolate its destination");
                Check((tracing.Stopped.Single().GetTagItem("db.query.text") != null) == activities, "Activity SQL followed the log flag");
                Check(!tracing.Stopped.Single().TagObjects.Any(t => t.Value?.ToString()?.Contains("signal-secret-1") == true), "Bindings became activity data");
            }
        });

        await test("diagnostics policy: legacy SQL alias stays log-only and respects activity sampling", async () =>
        {
            using var fixture = new Fixture("legacy"); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            Activity? stopped = null;
            using var listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == SqlLinqerDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
                ActivityStopped = a => stopped = a
            };
            ActivitySource.AddActivityListener(listener);
            var options = new SqlLinqerDiagnosticsOptions { IncludeSql = true, IncludeSqlInActivities = true };
            Check(options.IncludeSqlInLogs, "Legacy alias lost source compatibility");
            (await Db(fixture, new(factory.CreateLogger("SqlLinqer"), options)).BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            Check(stopped != null && stopped.GetTagItem("db.query.text") == null && sink.Entries.Any(e => e.Id.Id == 1002), "Propagation-only listener collected SQL or disabled logs");
        });

        await test("diagnostics policy: execution IDs distinguish page/count and parallel commands without tracing", async () =>
        {
            using var fixture = new Fixture("correlate"); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            var db = Db(fixture, new(factory.CreateLogger("SqlLinqer"), new() { IncludeSql = true }));
            var page = await db.BeginSelect<User>().SelectRootColumns().Tag("users.page").OrderBy(x => x.Id, SqlDir.ASC).Page(1, 2).ExecuteWithTotalCountAsync();
            Check(page.EnsureSuccess().TotalResults == 3, "Page result changed");
            var ended = sink.Entries.Where(e => e.Id.Id == 1001).ToArray();
            Check(ended.Length == 2 && ended.Select(e => e.State["CommandRole"]).ToHashSet().SetEquals(new object?[] { "Select", "Count" }), "Compiler role metadata missing");
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
                (await db.BeginSelect<User>().SelectRootColumns().Tag("users.parallel").ExecuteAsync()).EnsureSuccess())));
            var groups = sink.Entries.GroupBy(e => (string)e.State["ExecutionId"]!).ToArray();
            Check(groups.Length == 10 && groups.All(g => g.Count(e => e.Id.Id == 1000) == 1 && g.Count(e => e.Id.Id == 1001) == 1 && g.Count(e => e.Id.Id == 1002) == 1), "Events crossed execution IDs");
            Check(groups.All(g => g.Key.Length == 32), "Execution ID was not stable without a listener");
        });

        await test("diagnostics policy: activity IDs match structured log correlation", async () =>
        {
            using var fixture = new Fixture("trace-correlation"); using var tracing = new TraceCapture(); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            using var parent = new Activity("http.request").Start();
            (await Db(fixture, new(factory.CreateLogger("SqlLinqer"))).BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            var activity = tracing.Stopped.Single();
            Check(activity.ParentId == parent.Id, "Request parent changed");
            Check(sink.Entries.All(e => Equals(e.State["TraceId"], activity.TraceId.ToString()) && Equals(e.State["SpanId"], activity.SpanId.ToString())
                && Equals(e.State["ExecutionId"], activity.GetTagItem("sqllinqer.execution_id"))), "Log and trace identities diverged");
        });

        await test("diagnostics policy: optional Information summaries and immutable per-execution settings", async () =>
        {
            using var fixture = new Fixture("snapshot"); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            var current = new SqlLinqerDiagnosticsOptions(); int reads = 0;
            var observer = new SqlLinqerDiagnostics(factory.CreateLogger("SqlLinqer"), null, () => { reads++; return current; });
            var db = Db(fixture, observer);
            await foreach (var _ in db.BeginSelect<User>().SelectRootColumns().Tag("old.settings").ExecuteAsyncStream())
            { current = new() { LogSuccessfulQueriesAtInformation = true, IncludeSqlInLogs = true }; }
            Check(sink.Entries.Last(e => e.Id.Id == 1001).Level == LogLevel.Debug, "Settings changed an already-started execution");
            (await db.BeginSelect<User>().SelectRootColumns().Tag("new.settings").ExecuteAsync()).EnsureSuccess();
            Check(reads == 2 && sink.Entries.Last(e => e.Id.Id == 1001).Level == LogLevel.Information && sink.Entries.Any(e => e.Id.Id == 1002), "New execution did not refresh settings");
            var construction = new SqlLinqerDiagnosticsOptions();
            var fixedObserver = new SqlLinqerDiagnostics(factory.CreateLogger("SqlLinqer"), construction);
            construction.IncludeSql = true;
            int before = sink.Entries.Count(e => e.Id.Id == 1002);
            (await Db(fixture, fixedObserver).BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            Check(sink.Entries.Count(e => e.Id.Id == 1002) == before, "Observer retained a mutable constructor options object");
        });

        await test("diagnostics policy: typed bounded invariant bindings and safe unsupported objects", async () =>
        {
            using var fixture = new Fixture("format"); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var query = new RenderedQuery("SELECT 1");
                object?[] values = [null, "", 12.5m, DateTime.SpecifyKind(new(2026, 10, 23, 1, 2, 3), DateTimeKind.Utc), new DateTimeOffset(2026, 10, 23, 1, 2, 3, TimeSpan.FromHours(2)), new byte[] { 1, 255 }, Array.Empty<byte>(), SampleEnum.Ready, new DateOnly(2026, 10, 23), new TimeOnly(1, 2, 3), new string('雪', 1000), new PoisonValue()];
                foreach (var value in values) query.Parameters.AddParameter(value);
                var db = Db(fixture, new(factory.CreateLogger("SqlLinqer"), new() { IncludeParameterValues = true, MaxParameterValueLength = 64 }));
                (await db.ExecuteReaderListAsync<int>(query)).EnsureSuccess();
                var parameters = (DiagnosticParameterSet)sink.Entries.Single(e => e.Id.Id == 1003).State["Parameters"]!;
                Check(parameters.Items[0].IsNull == true && parameters.Items[0].DisplayValue == "NULL" && parameters.Items[1].DisplayValue == "\"\"", "NULL/empty semantics lost");
                Check(parameters.Items[2].DisplayValue == "12.5" && parameters.Items[3].DisplayValue!.EndsWith('Z') && parameters.Items[4].DisplayValue!.EndsWith("+02:00"), "Numeric/date formatting was culture-sensitive");
                Check(parameters.Items[5].DisplayValue == "0x01FF" && parameters.Items[6].DisplayValue == "0x" && parameters.Items[7].DisplayValue == "Ready", "Binary/enum representation changed");
                Check(parameters.Items[8].DisplayValue == "2026-10-23" && parameters.Items[9].DisplayValue!.StartsWith("01:02:03"), "Modern date/time representation missing");
                Check(parameters.Items[10].Truncated && parameters.Items.All(p => p.DisplayValue!.Length <= 64), "Long value was not bounded");
                Check(parameters.Items[11].DisplayValue == "<unsupported>", "Unknown object formatter ran arbitrary ToString");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });

        await test("diagnostics policy: redacted parameter metadata retains DbType and counts omissions", async () =>
        {
            using var fixture = new Fixture("metadata"); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            var query = new RenderedQuery("SELECT 1");
            query.Parameters.AddParameter("private-value", System.Data.DbType.AnsiString);
            query.Parameters.AddParameter(42); query.Parameters.AddParameter(43);
            (await Db(fixture, new(factory.CreateLogger("SqlLinqer"), new() { MaxParameterCount = 1 })).ExecuteReaderListAsync<int>(query)).EnsureSuccess();
            var parameters = (DiagnosticParameterSet)sink.Entries.Single(e => e.Id.Id == 1003).State["Parameters"]!;
            Check(parameters.Items.Count == 1 && parameters.OmittedCount == 2 && parameters.Items[0].DbType == "AnsiString" && parameters.Items[0].ClrType == "System.String", "Typed metadata or omitted count missing");
            Check(parameters.Items[0].DisplayValue == null && parameters.Items[0].IsNull == null && !sink.Text.Contains("private-value"), "Metadata enabled values");
        });

        await test("diagnostics policy: slow warnings use Execute time and exclude paused stream consumers", async () =>
        {
            using var fixture = new Fixture("slow"); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            (await Db(fixture, new(factory.CreateLogger("SqlLinqer"), new() { SlowCommandThreshold = TimeSpan.FromTicks(1) })).BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            var slow = sink.Entries.Single(e => e.Id.Id == 1004);
            Check(slow.Level == LogLevel.Warning && Equals(slow.State["TimingPhase"], "ProviderExecute"), "Slow warning did not identify its measurement");
            var db = Db(fixture, new(factory.CreateLogger("SqlLinqer"), new() { SlowCommandThreshold = TimeSpan.FromSeconds(1) }));
            await foreach (var _ in db.BeginSelect<User>().SelectRootColumns().Tag("consumer.pause").ExecuteAsyncStream()) await Task.Delay(400);
            var ended = sink.Entries.Last(e => e.Id.Id == 1001);
            Check((double)ended.State["DurationMs"]! >= 1000 && sink.Entries.Count(e => e.Id.Id == 1004) == 1, "Consumer pauses produced a database slow warning");
        });

        await test("diagnostics policy: timeout and mapping errors classify without raw exception detail", async () =>
        {
            using var fixture = new Fixture("errors"); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            var db = Db(fixture, new(factory.CreateLogger("SqlLinqer")));
            db.Options.CancelCommand = command => SQLitePCL.raw.sqlite3_interrupt(((SqliteConnection)command.Connection!).Handle);
            var timed = await db.ExecuteReaderListAsync<long>(new("WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<3000000) SELECT SUM(x) FROM n"), commandTimeout: TimeSpan.FromMilliseconds(20));
            Check(timed.State == ResponseState.Error && Equals(sink.Entries.Last(e => e.Id.Id == 1001).State["FailureKind"], "Timeout"), "Timeout classification missing");
            var mapped = await db.ExecuteReaderModelAsync<User>(new("SELECT 'not-an-integer' AS Id, 'private-name' AS Name"));
            Check(mapped.State == ResponseState.Error && Equals(sink.Entries.Last(e => e.Id.Id == 1001).State["FailureKind"], "Mapping"), "Mapping classification missing");
            Check(sink.Entries.Where(e => e.Level == LogLevel.Error).All(e => e.Exception == null) && !sink.Text.Contains("private-name"), "Default failure detail exposed data");
        });

        await test("diagnostics policy: invalid runtime configuration falls back to redacted defaults and retains errors", async () =>
        {
            using var fixture = new Fixture("config-secret"); using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            bool invalid = false;
            var db = Db(fixture, new SqlLinqerDiagnostics(factory.CreateLogger("SqlLinqer"), null,
                () => invalid ? throw new InvalidOperationException("config-secret") : new() { IncludeSqlInLogs = true }));
            (await db.BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            int before = sink.Entries.Count(e => e.Id.Id == 1002);
            invalid = true;
            (await db.BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            var failed = await db.ExecuteReaderListAsync<int>(new("SELECT Missing FROM MissingTable"));
            Check(failed.State == ResponseState.Error && sink.Entries.Last(e => e.Id.Id == 1001).Level == LogLevel.Error, "Invalid config suppressed query errors");
            Check(sink.Entries.Count(e => e.Id.Id == 1005) == 1 && sink.Entries.Count(e => e.Id.Id == 1002) == before && !sink.Text.Contains("config-secret"), "Invalid config retained raw detail or flooded warnings");
            invalid = false;
            (await db.BeginSelect<User>().SelectRootColumns().ExecuteAsync()).EnsureSuccess();
            Check(sink.Entries.Count(e => e.Id.Id == 1002) == before + 1, "Valid configuration did not recover");
        });

        await test("diagnostics policy: wrapped provider timeouts retain provider type and classify without messages", async () =>
        {
            using var sink = new FlowLogProvider(); using var factory = Factory(sink);
            var db = new ConnectionFactoryConnector(() => throw new WrappedProviderTimeout(), DbFlavor.PostgreSql,
                new() { Observer = new SqlLinqerDiagnostics(factory.CreateLogger("SqlLinqer")) });
            var result = await db.ExecuteReaderListAsync<int>(new("SELECT 1"));
            var ended = sink.Entries.Single(e => e.Id.Id == 1001);
            Check(result.State == ResponseState.Error && Equals(ended.State["ErrorType"], typeof(WrappedProviderTimeout).FullName)
                && Equals(ended.State["FailureKind"], "Timeout") && ended.State["CommandDurationMs"] == null, "Nested provider timeout lost its classification or invented Execute time");
        });

        await test("diagnostics policy: invalid limits fail during observer construction", () =>
        {
            foreach (var options in new[] { new SqlLinqerDiagnosticsOptions { MaxParameterCount = 0 }, new() { MaxParameterValueLength = 4 }, new() { SlowCommandThreshold = TimeSpan.Zero } })
            {
                bool threw = false;
                try { _ = new SqlLinqerDiagnostics(options: options); } catch (ArgumentOutOfRangeException) { threw = true; }
                Check(threw, "Invalid diagnostics settings were accepted");
            }
            return Task.CompletedTask;
        });
    }
    private static ILoggerFactory Factory(FlowLogProvider sink) => LoggerFactory.Create(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(sink));
    private static ConnectionFactoryConnector Db(Fixture fixture, SqlLinqerDiagnostics observer) => new(fixture.Connect, DbFlavor.Sqlite, new() { Observer = observer });
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class WrappedProviderTimeout : System.Data.Common.DbException
    { internal WrappedProviderTimeout() : base("private-provider-message", new TimeoutException("private-timeout")) { } }
    private enum SampleEnum { Ready }
    private sealed class PoisonValue { public override string ToString() => throw new Exception("ToString must not run"); }
}
