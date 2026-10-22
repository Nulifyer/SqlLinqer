using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using Row = SqlLinqer.Tests.Regression.CompilationRegressionTests.Row;
using Parent = SqlLinqer.Tests.Regression.CompilationRegressionTests.Parent;

namespace SqlLinqer.Tests.Regression;

internal static class AsyncProviderChecks
{
    internal static void Run(DbFlavor flavor, Action<string, Action> test)
    {
        test($"real {flavor} sync/async scalar nulls, nullable values, GUIDs, enums, and invariant conversion", () =>
        {
            var connector = new ConnectionFactoryConnector(() => flavor == DbFlavor.Sqlite
                ? new SqliteConnection("Data Source=:memory:") : new DuckDBConnection("Data Source=:memory:"), flavor);
            ProductionRegressionTests.CheckScalars(connector).GetAwaiter().GetResult();
        });
        test($"real {flavor} sync/async CRUD, totals, nesting, binary values, and caller rollback", () => CheckProvider(flavor).GetAwaiter().GetResult());
    }

    private static async Task CheckProvider(DbFlavor flavor)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sqllinqer-async-{Guid.NewGuid():N}.db");
        try
        {
            var connector = new ConnectionFactoryConnector(() => flavor == DbFlavor.Sqlite
                ? new SqliteConnection($"Data Source={path};Pooling=False") : new DuckDBConnection($"Data Source={path}"), flavor);
            string identity = flavor == DbFlavor.Sqlite ? "INTEGER PRIMARY KEY" : "INTEGER PRIMARY KEY DEFAULT nextval('async_ids')";
            if (flavor == DbFlavor.DuckDb)
                Result(await connector.ExecuteNonQueryAsync(new RenderedQuery("CREATE SEQUENCE async_ids START 100")));
            Result(await connector.ExecuteNonQueryAsync(new RenderedQuery($"CREATE TABLE snapshot_rows (Id {identity}, Name VARCHAR, Payload BLOB)")));
            var inserted = await new InsertWithPrimaryKeyQuery<Row, int>().InsertAuto(new Row { Name = "雪", Payload = [0, 128, 255] }).ExecuteAsync(connector);
            Check(Result(inserted) == 1, "Inserted row count differs");
            int id = inserted.TouchedPrimaryKeys.Single();
            var syncRow = Result(new SelectQuery<Row>().SelectRootColumns().Where(x => x.Id, id, SqlOp.EQ).Execute(connector)).Single();
            var asyncRow = Result(await new SelectQuery<Row>().SelectRootColumns().Where(x => x.Id, id, SqlOp.EQ).ExecuteAsync(connector)).Single();
            Check(asyncRow.Name == syncRow.Name && asyncRow.Payload.SequenceEqual(syncRow.Payload)
                && asyncRow.Payload.SequenceEqual(new byte[] { 0, 128, 255 }), "Async JSON mapping differs");
            Result(new InsertQuery<Row>().InsertAuto(new Row { Name = "second", Payload = [9] }).Execute(connector));
            var page = await new SelectQuery<Row>().SelectRootColumns().Top(1).ExecuteWithTotalCountAsync(connector);
            Check(Result(page).Count == 1 && page.TotalResults == 2, "Async total differs");
            var aggregate = Result(await new SelectQueryInitializer<Row>().BuildQuery(x => new { Count = x.Count() }).ExecuteAsync(connector));
            Check(aggregate.Single().Count == 2, "Async aggregate mapping differs");
            using (var output = Result(await new UpdateQuery<Row>().Update(x => x.Name, "changed").Where(x => x.Id, id, SqlOp.EQ)
                .Output(x => x.Name).ExecuteWithOutputAsync(connector)))
                Check(output.Rows.Count == 1 && (string)output.Rows[0][0] == "changed", "Async output differs");
            var tabular = new SelectQuery<Row>().SelectRootColumns().Where(x => x.Id, id, SqlOp.EQ);
            var rendered = new RenderedQuery(); rendered.Text = tabular.Render(rendered.Parameters, flavor, false);
            using (var table = Result(await connector.ExecuteReaderDataTableAsync(rendered)))
                Check(((byte[])table.Rows[0][nameof(Row.Payload)]).SequenceEqual(new byte[] { 0, 128, 255 }), "Async tabular binary values differ");
            var tabularSync = Result(connector.ExecuteReaderList<Row>(rendered)).Single();
            var tabularAsync = Result(await connector.ExecuteReaderListAsync<Row>(rendered)).Single();
            Check(tabularSync.Id == id && tabularAsync.Id == id
                && tabularSync.Name == "changed" && tabularAsync.Name == "changed"
                && tabularSync.Payload.SequenceEqual(new byte[] { 0, 128, 255 })
                && tabularAsync.Payload.SequenceEqual(tabularSync.Payload), "Tabular model lists changed native values");

            using (var connection = connector.CreateConnection())
            {
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();
                Result(await new InsertQuery<Row>().InsertAuto(new Row { Name = "rollback" }).ExecuteAsync(connector, transaction));
                Check(connection.State == System.Data.ConnectionState.Open, "Caller connection was closed");
                transaction.Rollback();
            }
            Check(Result(await new SelectQuery<Row>().SelectRootColumns().Where(x => x.Name, "rollback", SqlOp.EQ).ExecuteAsync(connector)).Count == 0,
                "Caller rollback was not preserved");

            Result(await connector.ExecuteNonQueryAsync(new RenderedQuery("CREATE TABLE snapshot_parents (Id INTEGER PRIMARY KEY); CREATE TABLE snapshot_children (Id INTEGER PRIMARY KEY, ParentId INTEGER, Name VARCHAR); INSERT INTO snapshot_parents VALUES (1),(2); INSERT INTO snapshot_children VALUES (1,1,'child')")));
            var nested = new SelectQuery<Parent>().SelectRootColumns().Select(x => x.Children).OrderBy(x => x.Id, SqlDir.ASC);
            var snapshot = nested.Capture();
            var children = Result(await nested.ExecuteAsync(connector));
            Check(children[0].Children.Single().Name == "child" && children[1].Children.Count == 0, "Nested collection contract differs");
            Check(Result(connector.ExecuteReaderJson<Parent>(snapshot.Render(flavor)))[1].Children.Count == 0, "Captured and async result shapes differ");
            var deleted = await new DeleteWithPrimaryKeyQuery<Row, int>().Where(x => x.Id, id, SqlOp.EQ).ExecuteAsync(connector);
            Check(Result(deleted) == 1 && deleted.TouchedPrimaryKeys.Single() == id, "Async deleted keys differ");
            Check(!Result(await new SelectPrimaryKeyExistsQuery<Row, int>(id).ExecuteAsync(connector)), "Deleted row still exists");

            if (flavor == DbFlavor.DuckDb)
            {
                connector.Options.CommandTimeout = TimeSpan.FromSeconds(2);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await AsyncRegressionTests.Canceled(() => connector.ExecuteScalarAsync<long>(
                    new RenderedQuery("SELECT sum(i)::BIGINT FROM range(10000000000) t(i)"), cancellationToken: cancellation.Token), cancellation.Token);
                Check(Result(await connector.ExecuteScalarAsync<int>(new RenderedQuery("SELECT 1"))) == 1, "Cancellation affected the next command");
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static T Result<T>(SqlResponse<T> response) => AsyncRegressionTests.Result(response);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
