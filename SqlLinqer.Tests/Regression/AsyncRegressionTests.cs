using System.Data;
using System.Data.Common;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using Row = SqlLinqer.Tests.Regression.CompilationRegressionTests.Row;

namespace SqlLinqer.Tests.Regression;

internal static class AsyncRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        void Async(string name, Func<Task> action) => test(name, () => action().GetAwaiter().GetResult());
        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
        {
            Async($"async select and total use async provider contracts for {flavor}", async () =>
            {
                var database = new FakeDatabase(); var connector = new Connector(database, flavor);
                IAsyncQueryExecutable<List<Row>> query = new SelectQuery<Row>().SelectRootColumns();
                var rows = await query.ExecuteAsync(connector);
                Check(rows.State == ResponseState.Valid && rows.Result.Count == 2, rows.Error?.ToString());
                var total = await new SelectQuery<Row>().SelectRootColumns().Page(2, 2).ExecuteWithTotalCountAsync(connector);
                Check(total.State == ResponseState.Valid && total.TotalResults == 22, total.Error?.ToString());
                Check(database.AsyncOpens == 3 && database.AsyncCommands == 3 && database.AsyncReads > 0
                    && database.SyncCommands == 0 && connector.AsyncInterceptions == 3 && connector.SyncInterceptions == 0,
                    "Async execution used synchronous command contracts");
                Check(database.Connections.All(c => c.IsDisposed && c.Commands.All(x => x.IsDisposed)), "Async resources leaked");
            });

            Async($"async output and primary key writes preserve response types for {flavor}", async () =>
            {
                var database = new FakeDatabase(); var connector = new Connector(database, flavor);
                if (flavor == DbFlavor.MySql)
                {
                    await Rejected(() => new InsertWithPrimaryKeyQuery<Row, int>().InsertAuto(new Row()).ExecuteAsync(connector));
                    await Rejected(() => new UpdateWithPrimaryKeyQuery<Row, int>().Update(x => x.Name, "changed").ExecuteAsync(connector));
                    await Rejected(() => new DeleteWithPrimaryKeyQuery<Row, int>().ExecuteAsync(connector));
                    Result(await new InsertQuery<Row>().InsertAuto(new Row()).ExecuteAsync(connector));
                    Result(await new UpdateQuery<Row>().Update(x => x.Name, "changed").ExecuteAsync(connector));
                    Result(await new DeleteQuery<Row>().Where(x => x.Id, 7, SqlOp.EQ).ExecuteAsync(connector));
                    Check(database.SyncCommands == 0, "Writes used synchronous execution");
                    return;
                }
                var insert = new InsertWithPrimaryKeyQuery<Row, int>().InsertAuto(new Row { Name = "first" });
                IAsyncQueryExecutableWithPrimaryKeys<int, int> executable = insert;
                var inserted = await executable.ExecuteAsync(connector);
                Check(inserted.State == ResponseState.Valid && inserted.TouchedPrimaryKeys.Single() == 7, inserted.Error?.ToString());
                var updated = await new UpdateWithPrimaryKeyQuery<Row, int>().Update(x => x.Name, "changed").Where(x => x.Id, 7, SqlOp.EQ).ExecuteAsync(connector);
                Check(updated.State == ResponseState.Valid && updated.TouchedPrimaryKeys.Single() == 7, updated.Error?.ToString());
                var deleted = await new DeleteWithPrimaryKeyQuery<Row, int>().Where(x => x.Id, 7, SqlOp.EQ).ExecuteAsync(connector);
                Check(deleted.State == ResponseState.Valid && deleted.TouchedPrimaryKeys.Single() == 7, deleted.Error?.ToString());
                using var output = Result(await new InsertQuery<Row>().InsertAuto(new Row { Name = "out" }).Output(x => x.Id).ExecuteWithOutputAsync(connector));
                Check(output.Rows.Count == 1 && (int)output.Rows[0][0] == 7, "Tabular output differs");
                Check(database.SyncCommands == 0, "Writes used synchronous command execution");
            });
        }

        Async("async execution retains its connector and supports a per-call override", async () =>
        {
            var first = new Connector(new FakeDatabase { Rows = 1 }, DbFlavor.PostgreSql);
            var second = new Connector(new FakeDatabase { Rows = 2 }, DbFlavor.PostgreSql);
            var fallback = new Connector(new FakeDatabase { Rows = 3 }, DbFlavor.PostgreSql);
            var previous = SqlLinqer.Default.Connector;
            try
            {
                var query = Item.BeginSelect(first).SelectRootColumns();
                SqlLinqer.Default.Connector = fallback;
                Check(Result(await query.ExecuteAsync()).Count == 1 && Result(await query.ExecuteAsync(second)).Count == 2
                    && Result(await query.ExecuteAsync()).Count == 1 && fallback.Created == 0, "Async connector binding differs");
            }
            finally { SqlLinqer.Default.Connector = previous; }
        });

        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
        {
            Async($"async truncate for {flavor}", async () =>
            {
                var database = new FakeDatabase(); var connector = new Connector(database, flavor);
                if (flavor == DbFlavor.Sqlite) await Rejected(() => new TruncateQuery<Row>().ExecuteAsync(connector));
                else Result(await new TruncateQuery<Row>().ExecuteAsync(connector));
                Check(database.SyncCommands == 0, "Async method used a synchronous command");
            });
        }

        Async("async SQL Server mapper combines JSON document chunks", async () =>
        {
            var database = new FakeDatabase { JsonRows = ["[{\"Id\":", "7,\"Name\":\"雪\"}]"] };
            var result = Result(await new SelectQuery<Row>().SelectRootColumns().ExecuteAsync(new FakeConnector(database, DbFlavor.SqlServer)));
            Check(result.Single().Id == 7 && result[0].Name == "雪" && database.AsyncReads == 3, "Chunked JSON mapping differs");
        });

        Async("async tabular loading preserves synchronous schema and row-state behavior", async () =>
        {
            var database = new FakeDatabase
            {
                ReaderFactory = _ =>
                {
                    var source = new DataTable();
                    var id = source.Columns.Add("Id", typeof(int)); id.AutoIncrement = true;
                    source.PrimaryKey = [id];
                    var name = source.Columns.Add("Name", typeof(string)); name.MaxLength = 40;
                    source.Rows.Add(7, "schema"); name.ReadOnly = true;
                    return source.CreateDataReader();
                }
            };
            var connector = new Connector(database, DbFlavor.SqlServer);
            using var synchronous = Result(connector.ExecuteReaderDataTable(new RenderedQuery("SELECT schema")));
            using var asynchronous = Result(await connector.ExecuteReaderDataTableAsync(new RenderedQuery("SELECT schema")));
            Check(asynchronous.PrimaryKey.Select(c => c.ColumnName).SequenceEqual(synchronous.PrimaryKey.Select(c => c.ColumnName))
                && asynchronous.Columns[0].AutoIncrement == synchronous.Columns[0].AutoIncrement
                && asynchronous.Columns[1].ReadOnly == synchronous.Columns[1].ReadOnly
                && asynchronous.Columns[1].MaxLength == synchronous.Columns[1].MaxLength
                && asynchronous.Rows[0].RowState == synchronous.Rows[0].RowState, "Async schema or row state differs");
        });

        Async("async count uses the captured definition after builder mutation during I/O", async () =>
        {
            var database = new FakeDatabase(); var connector = new Connector(database, DbFlavor.PostgreSql);
            var query = new SelectQuery<Row>().SelectRootColumns().Where(x => x.Name, "original", SqlOp.EQ).Page(2, 2);
            database.BeforeAsyncCommand = _ => { query.Top(0).Where(x => x.Id, 99, SqlOp.EQ); return Task.CompletedTask; };
            var result = await query.ExecuteWithTotalCountAsync(connector);
            Check(result.State == ResponseState.Valid && result.TotalResults == 22 && database.Executed.Count == 2, result.Error?.ToString());
            Check(database.Executed.All(c => c.Parameters.Count == 1 && c.Parameters[0].Value.Equals("original")), "Count used the changed builder");
        });

        foreach (bool initiallyPaged in new[] { false, true })
        {
            Async($"async aggregate count retains captured paging, initiallyPaged={initiallyPaged}", async () =>
            {
                var database = new FakeDatabase(); var connector = new Connector(database, DbFlavor.PostgreSql);
                var query = new SelectQueryInitializer<Row>()
                    .BuildQuery(x => new { Id = x.Select(y => y.Id) }).Top(initiallyPaged ? 1 : 0);
                database.BeforeAsyncCommand = _ => { query.Top(initiallyPaged ? 0 : 1); return Task.CompletedTask; };
                var result = await query.ExecuteWithTotalCountAsync(connector);
                Check(result.State == ResponseState.Valid, result.Error?.ToString());
                Check(result.TotalResults == (initiallyPaged ? database.Total : database.Rows)
                    && database.Executed.Count == (initiallyPaged ? 2 : 1), "Aggregate count used changed paging");
            });
        }

        Async("async batches freeze entity values and use one owned transaction", async () =>
        {
            var database = new FakeDatabase(); var connector = new Connector(database, DbFlavor.PostgreSql);
            connector.Options.ParameterLimit = 2;
            var source = new[] { new Row { Name = "one" }, new Row { Name = "two" }, new Row { Name = "three" } };
            var query = new InsertQuery<Row>().InsertAuto(source);
            database.BeforeAsyncCommand = _ => { source[1].Name = "mutated"; query.InsertAuto(new Row { Name = "extra" }); return Task.CompletedTask; };
            var result = await query.ExecuteAsync(connector);
            Check(result.State == ResponseState.Valid && result.Result == 3 && database.Executed.Count == 3, result.Error?.ToString());
            Check(database.Executed.SelectMany(c => c.Parameters.Cast<DbParameter>()).Any(p => Equals(p.Value, "two"))
                && !database.Executed.SelectMany(c => c.Parameters.Cast<DbParameter>()).Any(p => Equals(p.Value, "mutated")), "Batch values changed across awaits");
            var connection = database.Connections.Single();
            Check(connection.Transactions.Single().Commits == 1 && connection.IsDisposed && database.SyncCommands == 0, "Batch ownership differs");
        });

        Async("async pre-cancellation never creates a connection", async () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var connector = new Connector(new FakeDatabase(), DbFlavor.PostgreSql);
            await Canceled(() => new SelectQuery<Row>().SelectRootColumns().ExecuteAsync(connector, cancellationToken: cancellation.Token), cancellation.Token);
            await Canceled(() => new InsertQuery<Row>().InsertAuto(new Row()).ExecuteAsync(connector, cancellationToken: cancellation.Token), cancellation.Token);
            Check(connector.Created == 0, "Pre-cancellation opened a connection");
        });

        foreach (bool caller in new[] { false, true })
        {
            Async($"async command cancellation preserves transaction ownership, caller={caller}", async () =>
            {
                using var cancellation = new CancellationTokenSource();
                var database = new FakeDatabase(); var connector = new Connector(database, DbFlavor.PostgreSql);
                database.BeforeAsyncCommand = _ => { cancellation.Cancel(); return Task.CompletedTask; };
                using var external = caller ? new FakeConnection(database) : null;
                external?.Open();
                using var transaction = external?.BeginTransaction();
                await Canceled(() => new UpdateQuery<Row>().Update(x => x.Name, "cancelled")
                    .ExecuteAsync(connector, transaction, cancellation.Token), cancellation.Token);
                var connection = database.Connections.Single(); var used = connection.Transactions.Single();
                Check(connection.Commands.Single().IsDisposed, "Cancelled command leaked");
                Check(caller ? !connection.IsDisposed && used.Commits == 0 && used.Rollbacks == 0
                    : connection.IsDisposed && used.Commits == 0 && used.Rollbacks == 1 && used.IsDisposed, "Cancellation changed ownership");
            });
        }

        Async("async reader cancellation propagates and disposes reader and command", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var database = new FakeDatabase(); var connector = new Connector(database, DbFlavor.PostgreSql);
            database.BeforeAsyncRead = _ => { cancellation.Cancel(); return Task.CompletedTask; };
            await Canceled(() => new SelectQuery<Row>().SelectRootColumns().ExecuteAsync(connector, cancellationToken: cancellation.Token), cancellation.Token);
            Check(database.DisposedReaders == 1 && database.Connections.Single().IsDisposed
                && database.Executed.Single().IsDisposed, "Reader cancellation leaked resources");
        });

        Async("async database errors remain response values and roll back owned writes", async () =>
        {
            var database = new FakeDatabase { FailAt = "execute" }; var connector = new Connector(database, DbFlavor.PostgreSql);
            var response = await new UpdateQuery<Row>().Update(x => x.Name, "failure").ExecuteAsync(connector);
            Check(response.State == ResponseState.Error && response.Error.ToString().Contains("execute"), "Database exception escaped response contract");
            Check(database.Connections.Single().Transactions.Single().Rollbacks == 1, "Owned write was not rolled back");
        });

        foreach (string stage in new[] { "open", "command", "parameter", "begin", "execute", "commit", "dispose-connection", "dispose-transaction" })
        {
            Async($"async owned write failures remain response errors at {stage}", async () =>
            {
                var database = new FakeDatabase { FailAt = stage };
                var connector = new Connector(database, DbFlavor.PostgreSql);
                var result = await new UpdateQuery<Row>().Update(x => x.Name, "failure").ExecuteAsync(connector);
                Check(result.State == ResponseState.Error && result.Error.ToString().Contains(stage), "Async failure stage was lost");
                Check(database.Connections.All(c => c.IsDisposed && c.Commands.All(command => command.IsDisposed)), "Failed async operation leaked owned resources");
            });
        }

        Async("async command and reader receive the supplied cancellation token", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var database = new FakeDatabase(); var connector = new Connector(database, DbFlavor.PostgreSql);
            Result(await new SelectQuery<Row>().SelectRootColumns().ExecuteAsync(connector, cancellationToken: cancellation.Token));
            Check(database.LastOpenToken == cancellation.Token && database.LastCommandToken == cancellation.Token
                && database.LastReadToken == cancellation.Token, "Provider did not receive the supplied token");
        });

        Async("async unrelated cancellation exceptions remain response errors", async () =>
        {
            var database = new FakeDatabase { BeforeAsyncCommand = _ => throw new OperationCanceledException("provider cancellation") };
            var response = await new SelectQuery<Row>().SelectRootColumns().ExecuteAsync(new Connector(database, DbFlavor.PostgreSql));
            Check(response.State == ResponseState.Error, "Unrequested provider cancellation changed response behavior");
        });

        foreach (DbFlavor flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
        {
            Async($"async TVP insert and primary keys use async commands for {flavor}", async () =>
            {
                var database = new FakeDatabase(); var connector = new Connector(database, flavor);
                var result = await new InsertWithPrimaryKeyQuery<Row, int>().InsertAuto(new Row { Name = "tvp" }).ExecuteTvpAsync(connector);
                Check(result.State == ResponseState.Valid && result.TouchedPrimaryKeys.Single() == 7, result.Error?.ToString());
                Check(database.AsyncCommands == 3 && database.SyncCommands == 0
                    && database.Connections.First().Transactions.Single().Commits == 1, "TVP lifecycle differs");
                TvpRegressionTests.CheckTypeDdl(database);
            });
        }

        Async("cancelled TVP cleans its type in a usable caller transaction", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var database = new FakeDatabase(); var connector = new Connector(database, DbFlavor.SqlServer);
            using var connection = new FakeConnection(database); connection.Open();
            using var transaction = connection.BeginTransaction();
            database.BeforeAsyncCommand = token =>
            {
                if (database.Executed.Count == 1 && token.CanBeCanceled) cancellation.Cancel();
                return Task.CompletedTask;
            };
            await Canceled(() => new InsertQuery<Row>().InsertAuto(new Row()).ExecuteTvpAsync(connector, transaction, cancellation.Token), cancellation.Token);
            Check(database.Executed.Count == 2 && database.Executed.Last().CommandText.StartsWith("DROP TYPE")
                && connection.Transactions.Single().Rollbacks == 0 && !connection.IsDisposed, "Caller cleanup was cancelled or completed its transaction");
        });

    }

    private static async Task Rejected(Func<Task> action)
    {
        try { await action(); }
        catch (NotSupportedException) { return; }
        throw new Exception("Provider output rejection differs from synchronous execution");
    }

    internal static async Task Canceled(Func<Task> action, CancellationToken token)
    {
        try { await action(); }
        catch (OperationCanceledException error) { Check(error.CancellationToken == token, "Cancellation token was lost"); return; }
        throw new Exception("Expected OperationCanceledException");
    }
    internal static T Result<T>(SqlResponse<T> response)
    {
        Check(response.State == ResponseState.Valid, response.Error?.ToString());
        return response.Result;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Connector(FakeDatabase database, DbFlavor flavor) : BaseConnector(flavor, new ConnectorOptions())
    {
        internal int Created, AsyncInterceptions, SyncInterceptions;
        public override DbConnection CreateConnection() { Created++; return new FakeConnection(database); }
        public override bool JsonResultsRowByRow() => true;
        protected override SqlResponse<T> ExecuteCommand<T>(RenderedQuery query, Func<SqlResponse<T>> action)
        { SyncInterceptions++; return base.ExecuteCommand(query, action); }
        protected override Task<SqlResponse<T>> ExecuteCommandAsync<T>(RenderedQuery query, Func<Task<SqlResponse<T>>> action, CancellationToken cancellationToken)
        { AsyncInterceptions++; return base.ExecuteCommandAsync(query, action, cancellationToken); }
    }
}
