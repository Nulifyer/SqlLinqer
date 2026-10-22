using System.Collections.Concurrent;
using System.Data;
using DuckDB.NET.Data;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

internal static class CommandTimeoutRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        void Async(string name, Func<Task> action) => test("command timeout: " + name, () => action().GetAwaiter().GetResult());
        foreach (string mode in new[] { "sync", "async", "stream" })
        {
            Async($"override, zero, rounding, and fallback, {mode}", async () =>
            {
                var database = new FakeDatabase { JsonRows = ["{\"Id\":7}"] };
                var connector = new FakeConnector(database);
                connector.Options.CommandTimeout = TimeSpan.FromSeconds(9);
                foreach (var (timeout, expected) in new (TimeSpan?, int)[]
                {
                    (null, 9), (TimeSpan.FromMinutes(3), 180), (TimeSpan.Zero, 0),
                    (TimeSpan.FromTicks(1), 1), (TimeSpan.FromMilliseconds(1500), 2), (null, 9)
                })
                {
                    await Read(connector, mode, timeout);
                    Check(database.Executed.Last().CommandTimeout == expected, "Command did not receive its effective timeout");
                    Check(connector.Options.CommandTimeout == TimeSpan.FromSeconds(9), "Override mutated the connector default");
                }
                Check(database.Connections.All(x => x.IsDisposed), "Execution retained an owned connection");
            });
            foreach (var timeout in new[] { TimeSpan.FromTicks(-1), TimeSpan.MaxValue })
            {
                Async($"invalid override rejected before opening, {mode}, ticks={timeout.Ticks}", async () =>
                {
                    var database = new FakeDatabase();
                    try { await Read(new FakeConnector(database), mode, timeout); }
                    catch (ArgumentOutOfRangeException error)
                    {
                        Check(error.ParamName == "commandTimeout" && database.Connections.Count == 0,
                            "Invalid timeout reached the connection factory");
                        return;
                    }
                    throw new Exception("Invalid timeout was accepted");
                });
            }
        }
        Async("concurrent overrides on a shared connector stay independent", async () =>
        {
            var databases = new ConcurrentBag<FakeDatabase>();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int arrivals = 0;
            var connector = new ConnectionFactoryConnector(() =>
            {
                var database = new FakeDatabase
                {
                    BeforeAsyncCommand = _ =>
                    {
                        if (Interlocked.Increment(ref arrivals) == 3) ready.SetResult();
                        return ready.Task;
                    }
                };
                databases.Add(database);
                return new FakeConnection(database);
            }, DbFlavor.PostgreSql, new ConnectorOptions { CommandTimeout = TimeSpan.FromSeconds(9) });
            var responses = await Task.WhenAll(
                connector.ExecuteNonQueryAsync(new("one"), commandTimeout: TimeSpan.FromSeconds(31)),
                connector.ExecuteNonQueryAsync(new("two"), commandTimeout: TimeSpan.FromSeconds(47)),
                connector.ExecuteNonQueryAsync(new("default"))).WaitAsync(TimeSpan.FromSeconds(10));
            Check(responses.All(x => x.State == ResponseState.Valid), "Concurrent execution failed");
            Check(databases.SelectMany(x => x.Executed).Select(x => x.CommandTimeout).Order().SequenceEqual(new[] { 9, 31, 47 }),
                "Concurrent executions shared an override");
            Check(connector.Options.CommandTimeout == TimeSpan.FromSeconds(9), "Concurrent override changed defaults");
        });
        Async("deferred typed and raw streams capture their effective timeout", async () =>
        {
            var database = new FakeDatabase { JsonRows = ["{\"Id\":7}"] };
            var connector = new FakeConnector(database);
            connector.Options.CommandTimeout = TimeSpan.FromSeconds(9);
            var query = new SelectQuery<TimeoutRow>().SelectRootColumns();
            var inherited = query.ExecuteAsyncStream(connector);
            var explicitStream = query.ExecuteAsyncStream(connector, commandTimeout: TimeSpan.FromSeconds(71));
            var raw = connector.ExecuteReaderJsonAsyncStream<TimeoutRow>(new("raw"));
            connector.Options.CommandTimeout = TimeSpan.FromSeconds(13);
            foreach (var sequence in new[] { inherited, explicitStream, raw })
                await foreach (var row in sequence) Check(row.Id == 7, "Stream changed its result");
            Check(database.Executed.Select(x => x.CommandTimeout).SequenceEqual(new[] { 9, 71, 9 }),
                "Deferred stream did not retain its timeout");
        });
        foreach (bool async in new[] { false, true })
        {
            Async($"page, count, batching, and TVP commands retain the override, async={async}", async () =>
            {
                var database = new FakeDatabase { JsonRows = ["[{\"Id\":7,\"Name\":\"page\"}]"] };
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                connector.Options.ParameterLimit = 1;
                var timeout = TimeSpan.FromSeconds(71);
                var query = new SelectQuery<TimeoutRow>().SelectRootColumns().Top(1);
                var page = async ? await query.ExecuteWithTotalCountAsync(connector, commandTimeout: timeout)
                    : query.ExecuteWithTotalCount(connector, commandTimeout: timeout);
                Check(page.State == ResponseState.Valid && database.Executed.Count == 2, page.Error?.ToString() ?? "Missing page/count command");
                Check(database.Executed.All(x => x.CommandTimeout == 71), "Page or count lost its override");
                database.Executed.Clear();
                var insert = new InsertQuery<TimeoutRow>().InsertAuto(Enumerable.Range(1, 3).Select(x => new TimeoutRow { Name = x.ToString() }));
                var result = async ? await insert.ExecuteAsync(connector, commandTimeout: timeout) : insert.Execute(connector, commandTimeout: timeout);
                Check(result.State == ResponseState.Valid && database.Executed.Count == 3, "Insert was not split into three batches");
                Check(database.Executed.All(x => x.CommandTimeout == 71), "A batch lost its override");
                database.Executed.Clear();
                result = async ? await insert.ExecuteTvpAsync(connector, commandTimeout: timeout) : insert.ExecuteTvp(connector, commandTimeout: timeout);
                Check(result.State == ResponseState.Valid && database.Executed.Count == 3, "TVP execution did not create, insert, and drop");
                Check(database.Executed.All(x => x.CommandTimeout == 71), "TVP DDL or insert lost its override");
            });
            Async($"TVP failure cleanup retains the override, async={async}", async () =>
            {
                var database = new FakeDatabase { FailSql = "INSERT INTO" };
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                var insert = new InsertQuery<TimeoutRow>().InsertAuto(new TimeoutRow { Name = "failed" });
                var response = async ? await insert.ExecuteTvpAsync(connector, commandTimeout: TimeSpan.FromSeconds(71))
                    : insert.ExecuteTvp(connector, commandTimeout: TimeSpan.FromSeconds(71));
                Check(response.State == ResponseState.Error && database.Executed.Last().CommandText.Contains("DROP TYPE"), "Failure did not clean up its type");
                Check(database.Executed.All(x => x.CommandTimeout == 71), "Cleanup lost its override");
            });
            Async($"stored procedure retains the override, async={async}", async () =>
            {
                var database = new FakeDatabase
                {
                    ReaderFactory = _ =>
                    {
                        var table = new DataTable();
                        table.Columns.Add("Id", typeof(long));
                        table.Columns.Add("Name", typeof(string));
                        table.Rows.Add(7L, "procedure");
                        return table.CreateDataReader();
                    }
                };
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                var procedure = new TimeoutProcedure();
                var response = async ? await procedure.ExecuteAsync(new TimeoutInput(), connector, commandTimeout: TimeSpan.FromSeconds(71))
                    : procedure.Execute(new TimeoutInput(), connector, commandTimeout: TimeSpan.FromSeconds(71));
                Check(response.State == ResponseState.Valid && database.Executed.Single().CommandTimeout == 71,
                    response.Error?.ToString() ?? "Procedure lost its override");
            });
        }
        foreach (string mode in new[] { "sync", "async", "stream" })
        {
            Async($"native DuckDB override controls its deadline, {mode}", async () =>
            {
                var connector = new ConnectionFactoryConnector(() => new DuckDBConnection("Data Source=:memory:"), DbFlavor.DuckDb,
                    new ConnectorOptions { CommandTimeout = TimeSpan.FromMilliseconds(1) });
                var bounded = new RenderedQuery("SELECT json_object('Id', sum(i)::BIGINT) FROM range(1000000) t(i)");
                Check((await Read(connector, mode, TimeSpan.FromMinutes(1), bounded)).Single().Id == 499999500000,
                    "Long override did not complete native work");
                var unbounded = new RenderedQuery("SELECT json_object('Id', sum(sin(i::DOUBLE))) FROM range(9223372036854775807) t(i)");
                try { await Read(connector, mode, null, unbounded); }
                catch (SqlLinqer.Exceptions.SqlResponseException error) when (error.InnerException is TimeoutException)
                {
                    Check((await Read(connector, mode, TimeSpan.Zero)).Single().Id == 7, "Zero override or connection reuse failed");
                    return;
                }
                throw new Exception("Native default deadline was not enforced");
            });
        }
    }

    private static async Task<List<TimeoutRow>> Read(BaseConnector connector, string mode, TimeSpan? timeout, RenderedQuery query = null)
    {
        query ??= new RenderedQuery("SELECT json_object('Id', 7)");
        if (mode == "stream")
        {
            var result = new List<TimeoutRow>();
            await foreach (var row in connector.ExecuteReaderJsonAsyncStream<TimeoutRow>(query, commandTimeout: timeout)) result.Add(row);
            return result;
        }
        var response = mode == "sync" ? connector.ExecuteReaderJson<TimeoutRow>(query, commandTimeout: timeout)
            : await connector.ExecuteReaderJsonAsync<TimeoutRow>(query, commandTimeout: timeout);
        if (response.State != ResponseState.Valid) throw response.Error;
        return response.Result;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    [SqlTable("timeout_rows"), SqlAutoInsert]
    private sealed class TimeoutRow
    {
        [SqlPrimaryKey] public long Id { get; set; }
        public string Name { get; set; }
    }
    private sealed class TimeoutInput { }
    [SqlProcedure("TimeoutProcedure")]
    private sealed class TimeoutProcedure : SqlLinqerCallable<TimeoutInput, TimeoutRow> { }
}
