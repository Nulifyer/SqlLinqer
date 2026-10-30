using System.Data;
using System.Text.Json;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Exceptions;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Queries.Typed;
using Row = SqlLinqer.Tests.Regression.CompilationRegressionTests.Row;
using Parent = SqlLinqer.Tests.Regression.CompilationRegressionTests.Parent;

namespace SqlLinqer.Tests.Regression;

internal static class StreamingRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        void Async(string name, Func<Task> run) => test("streaming " + name, () => run().GetAwaiter().GetResult());
        foreach (var flavor in Enum.GetValues<DbFlavor>())
        {
            Async($"mapping, repeat enumeration, and resources for {flavor}", async () =>
            {
                var db = new FakeDatabase { JsonRows = JsonRows(flavor) };
                var connector = new FakeConnector(db, flavor);
                IAsyncQueryExecutableStream<Row> query = new SelectQuery<Row>().SelectRootColumns();
                var stream = query.ExecuteAsyncStream(connector);
                Check(connector.Created == 0, "Stream creation opened a connection");
                var rows = await Collect(stream);
                Check(rows.Count == 2 && rows[0].Id == 1 && rows[0].Name == "雪😀" && rows[1].Name == null,
                    "Mapped rows differ");
                Check((db.LastReaderBehavior & CommandBehavior.SequentialAccess) != 0, "Sequential access was not requested");
                Check(db.Executed.Single().UseStreamingMode == (flavor == DbFlavor.DuckDb), "Native DuckDB streaming mode differs");
                Check((await Collect(stream)).Count == 2 && connector.Created == 2, "Repeated enumeration did not rerun");
                Disposed(db);
            });
            Async($"early exit disposes reader and owned connection for {flavor}", async () =>
            {
                var db = new FakeDatabase { JsonRows = JsonRows(flavor) };
                await foreach (var row in new SelectQuery<Row>().SelectRootColumns().ExecuteAsyncStream(new FakeConnector(db, flavor)))
                    break;
                Disposed(db);
            });
            Async($"empty JSON results for {flavor}", async () =>
            {
                var db = new FakeDatabase { JsonRows = IsDocument(flavor) ? ["[]"] : [] };
                Check((await Collect(new FakeConnector(db, flavor).ExecuteReaderModelAsyncStream<Row>(new("empty")))).Count == 0, "Empty result differs");
                Disposed(db);
            });
        }

        Async("first row does not read the rest of a row-form result", async () =>
        {
            var db = new FakeDatabase { Rows = 1000 };
            db.BeforeAsyncRead = _ => db.AsyncReads == 0 ? Task.CompletedTask
                : throw new Exception("Read beyond the requested row");
            await foreach (var row in new FakeConnector(db).ExecuteReaderModelAsyncStream<Row>(new("SELECT json"))) break;
            Check(db.AsyncReads == 1, "First yield required multiple reads");
            Disposed(db);
        });

        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            Async($"zero JSON transport rows match buffered execution for {flavor}", async () =>
            {
                var db = new FakeDatabase { JsonRows = [] };
                var connector = new FakeConnector(db, flavor);
                var query = new SelectQuery<Row>().SelectRootColumns();
                Check(AsyncRegressionTests.Result(query.Execute(connector)).Count == 0, "Sync empty transport differs");
                Check(AsyncRegressionTests.Result(await query.ExecuteAsync(connector)).Count == 0, "Async empty transport differs");
                Check((await Collect(query.ExecuteAsyncStream(connector))).Count == 0, "Stream empty transport differs");
                Check(db.Connections.All(c => c.IsDisposed && c.Commands.All(x => x.IsDisposed)), "Empty transport leaked owned resources");
            });

        Async("captures builder values and connector before deferred enumeration", async () =>
        {
            var db = new FakeDatabase();
            var connector = new FakeConnector(db);
            var query = new SelectQuery<Row>().SelectRootColumns().Where(x => x.Name, "original", SqlOp.EQ);
            var stream = query.ExecuteAsyncStream(connector);
            query.Where(x => x.Id, 999, SqlOp.EQ).Top(1);
            await Collect(stream);
            await Collect(stream);
            Check(db.Executed.All(c => c.Parameters.Count == 1 && c.Parameters[0].Value.Equals("original")
                && !c.CommandText.Contains("999") && !c.CommandText.Contains("LIMIT 1")), "Builder mutation reached the stream");
            Check(!ReferenceEquals(db.Executed[0].Parameters[0], db.Executed[1].Parameters[0]), "Bindings reused across enumerations");
        });

        Async("caller transaction survives early disposal", async () =>
        {
            var db = new FakeDatabase();
            using var connection = new FakeConnection(db); connection.Open();
            using var transaction = (FakeTransaction)connection.BeginTransaction();
            await foreach (var row in new FakeConnector(db).ExecuteReaderModelAsyncStream<Row>(new("SELECT json"), transaction)) break;
            Check(!connection.IsDisposed && !transaction.IsDisposed && transaction.Commits == 0 && transaction.Rollbacks == 0,
                "Library completed caller transaction");
            Check(db.Executed.Single().UsedTransaction == transaction && db.DisposedReaders == 1, "Reader or transaction binding differs");
        });

        foreach (string stage in new[] { "open", "command", "parameter", "execute", "read", "json", "dispose-connection" })
        {
            Async($"wraps {stage} failure and disposes resources", async () =>
            {
                var db = new FakeDatabase { FailAt = stage };
                if (stage == "read") db.BeforeAsyncRead = _ => throw new InvalidOperationException("read");
                if (stage == "json") db.JsonRows = ["{invalid}"];
                var query = new RenderedQuery("SELECT json"); query.Parameters.AddParameter(1);
                var error = await Error<SqlResponseException>(() => Collect(new FakeConnector(db).ExecuteReaderModelAsyncStream<Row>(query)));
                Check(ReferenceEquals(error.Query, query) && error.InnerException != null, "Query context was lost");
                Check(db.Connections.All(c => c.IsDisposed && c.Commands.All(x => x.IsDisposed)), "Failure leaked owned resources");
                if (stage is "read" or "json") Check(db.DisposedReaders == 1, "Failure leaked reader");
            });
        }

        Async("error after a delivered row preserves partial progress", async () =>
        {
            var db = new FakeDatabase { JsonRows = ["{\"Id\":1}", "{bad}"] };
            int delivered = 0;
            await Error<SqlResponseException>(async () =>
            {
                await foreach (var row in new FakeConnector(db).ExecuteReaderModelAsyncStream<Row>(new("partial"))) delivered++;
            });
            Check(delivered == 1, "Failure erased or concealed partial progress");
            Disposed(db);
        });

        foreach (bool enumerationToken in new[] { false, true })
        {
            Async($"cancellation during enumeration, enumerationToken={enumerationToken}", async () =>
            {
                using var cancellation = new CancellationTokenSource();
                var db = new FakeDatabase();
                var stream = new SelectQuery<Row>().SelectRootColumns().ExecuteAsyncStream(new FakeConnector(db),
                    cancellationToken: enumerationToken ? default : cancellation.Token);
                var error = await Error<OperationCanceledException>(async () =>
                {
                    await foreach (var row in stream.WithCancellation(enumerationToken ? cancellation.Token : default)) cancellation.Cancel();
                });
                Check(error.CancellationToken == cancellation.Token, "Cancellation token was lost");
                Disposed(db);
            });
        }

        Async("cancelled before enumeration does not create a connection", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var connector = new FakeConnector(new());
            var stream = new SelectQuery<Row>().SelectRootColumns().ExecuteAsyncStream(connector);
            cancellation.Cancel();
            await Error<OperationCanceledException>(() => Collect(stream.WithCancellation(cancellation.Token)));
            Check(connector.Created == 0, "Cancelled stream opened a connection");
        });

        Async("consumer failure disposes enumeration", async () =>
        {
            var db = new FakeDatabase();
            await Error<ApplicationException>(async () =>
            {
                await foreach (var row in new FakeConnector(db).ExecuteReaderModelAsyncStream<Row>(new("consumer"))) throw new ApplicationException("consumer");
            });
            Disposed(db);
        });

        Async("streaming hook covers deferred reads and disposal", async () =>
        {
            var connector = new HookConnector(new());
            var stream = connector.ExecuteReaderModelAsyncStream<Row>(new("hook"));
            Check(connector.Starts == 0, "Hook executed before enumeration");
            await foreach (var row in stream) break;
            Check(connector.Starts == 1 && connector.Ends == 1, "Hook missed early disposal");
        });

        Async("DuckDB deadline remains active while consumer pauses", async () =>
        {
            var db = new FakeDatabase();
            var connector = new FakeConnector(db, DbFlavor.DuckDb);
            connector.Options.CommandTimeout = TimeSpan.FromMilliseconds(30);
            var error = await Error<SqlResponseException>(async () =>
            {
                await foreach (var row in connector.ExecuteReaderModelAsyncStream<Row>(new("deadline"))) await Task.Delay(70);
            });
            Check(error.InnerException is TimeoutException, "Deadline was lost across yield");
            Disposed(db);
        });

        foreach (var flavor in new[] { DbFlavor.Sqlite, DbFlavor.DuckDb })
            Async($"real {flavor} nested mapping, projections, cancellation, and caller rollback", () => RealProvider(flavor));
    }

    private static async Task RealProvider(DbFlavor flavor)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sqllinqer-stream-{Guid.NewGuid():N}.db");
        try
        {
            var connector = new ConnectionFactoryConnector(() => flavor == DbFlavor.Sqlite
                ? new SqliteConnection($"Data Source={path};Pooling=False") : new DuckDBConnection($"Data Source={path}"), flavor);
            string blob = flavor == DbFlavor.Sqlite ? "X'0080FF'" : "from_hex('0080FF')";
            AsyncRegressionTests.Result(await connector.ExecuteNonQueryAsync(new("CREATE TABLE snapshot_rows (Id INTEGER PRIMARY KEY, Name VARCHAR, Payload BLOB); " +
                $"INSERT INTO snapshot_rows VALUES (1, '雪😀', {blob}),(2,NULL,NULL); " +
                "CREATE TABLE snapshot_parents (Id INTEGER PRIMARY KEY); CREATE TABLE snapshot_children (Id INTEGER PRIMARY KEY, ParentId INTEGER, Name VARCHAR); " +
                "INSERT INTO snapshot_parents VALUES (1),(2); INSERT INTO snapshot_children VALUES (1,1,'child')")));
            var query = new SelectQuery<Row>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC);
            var buffered = AsyncRegressionTests.Result(await query.ExecuteAsync(connector));
            var streamed = await Collect(query.ExecuteAsyncStream(connector));
            Check(streamed.Select(x => (x.Id, x.Name)).SequenceEqual(buffered.Select(x => (x.Id, x.Name))), "Real rows differ");
            Check(streamed[0].Payload.SequenceEqual(new byte[] { 0, 128, 255 }), "Streamed binary value differs");
            var attributed = await Collect(new SelectQuery<ApiRow>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).ExecuteAsyncStream(connector));
            Check(attributed[0].Id == 1 && attributed[0].Name == "雪😀", "API JSON attributes changed SQL mapping");
            var parents = await Collect(new SelectQuery<Parent>().SelectRootColumns().Select(x => x.Children).OrderBy(x => x.Id, SqlDir.ASC).ExecuteAsyncStream(connector));
            Check(parents[0].Children.Single().Name == "child" && parents[1].Children.Count == 0, "Nested JSON differs");
            var projection = await Collect(new SelectQueryInitializer<Row>().BuildQuery(x => new { Id = x.Select(y => y.Id) }).ExecuteAsyncStream(connector));
            Check(projection.Select(x => x.Id).Order().SequenceEqual(new[] { 1, 2 }), "Projection mapping differs");
            using var connection = connector.CreateConnection(); await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            { command.Transaction = transaction; command.CommandText = "INSERT INTO snapshot_rows VALUES (3,'rollback',NULL)"; command.ExecuteNonQuery(); }
            using (var cancellation = new CancellationTokenSource())
            {
                await Error<OperationCanceledException>(async () =>
                {
                    await foreach (var row in query.ExecuteAsyncStream(connector, transaction, cancellation.Token)) cancellation.Cancel();
                });
            }
            Check(connection.State == ConnectionState.Open, "Caller connection closed");
            // An interrupt registration must not survive into the next command.
            Check((await Collect(query.ExecuteAsyncStream(connector, transaction))).Count == 3, "Disposed stream affected next command");
            transaction.Rollback();
            Check((await Collect(query.ExecuteAsyncStream(connector))).Count == 2, "Caller rollback failed");
        }
        finally { File.Delete(path); }
    }

    private static bool IsDocument(DbFlavor flavor) => flavor is DbFlavor.SqlServer or DbFlavor.AzureSql;
    private static string[] JsonRows(DbFlavor flavor)
    {
        const string first = "{\"Id\":1,\"Name\":\"雪😀\"}", second = "{\"Id\":2,\"Name\":null}";
        return [first, second];
    }
    private static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> stream)
    { var rows = new List<T>(); await foreach (var row in stream) rows.Add(row); return rows; }
    private static async Task<List<T>> Collect<T>(System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable<T> stream)
    { var rows = new List<T>(); await foreach (var row in stream) rows.Add(row); return rows; }
    private static async Task<T> Error<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Disposed(FakeDatabase db)
    {
        Check(db.Connections.All(c => c.IsDisposed && c.Commands.All(x => x.IsDisposed)), "Owned resources leaked");
        Check(db.DisposedReaders == db.AsyncCommands, "Reader leaked");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    [SqlLinqer.Modeling.SqlTable("snapshot_rows")]
    private sealed class ApiRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("api_id")]
        [SqlLinqer.Modeling.SqlPrimaryKey] public int Id { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public string Name { get; set; }
    }
    private sealed class HookConnector(FakeDatabase db) : BaseConnector(DbFlavor.PostgreSql, new())
    {
        internal int Starts, Ends;
        public override System.Data.Common.DbConnection CreateConnection() => new FakeConnection(db);
        protected override async IAsyncEnumerable<T> ExecuteCommandAsyncStream<T>(RenderedQuery query,
            Func<CancellationToken, IAsyncEnumerable<T>> action,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Starts++;
            try { await foreach (var row in base.ExecuteCommandAsyncStream(query, action, cancellationToken)) yield return row; }
            finally { Ends++; }
        }
    }
}
