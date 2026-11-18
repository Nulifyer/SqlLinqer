using System.Data;
using SqlLinqer.Components.Update;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

// Runs typed builders through a connector bound to one open connection, on every live provider.
internal static class SessionConnectionProviderChecks
{
    internal static void Run(BaseConnector db, Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
            test($"session connector: typed queries share one open connection, async={asynchronous}", () => CheckProvider(db, asynchronous).GetAwaiter().GetResult());
    }

    private static async Task CheckProvider(BaseConnector db, bool asynchronous)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        bool sqlServer = db.DbFlavor is DbFlavor.SqlServer or DbFlavor.AzureSql;
        // A temporary table exists only in its session, so every typed query that succeeds ran on the supplied connection.
        // The queries are built without a default schema, because a temporary table has none.
        using var connection = db.OpenConnection();
        var session = db.OnConnection(connection);
        Check(ReferenceEquals(session.Options, db.Options) && session.DbFlavor == db.DbFlavor && session.GetType() == db.GetType(),
            "The session connector is not a copy of its source connector");
        session.BeginSql($"CREATE {(sqlServer ? "" : "TEMPORARY ")}TABLE {Q("#session_rows")} ({Q("id")} INTEGER PRIMARY KEY, {Q("label")} VARCHAR(40) NULL)")
            .WithoutTransaction().Execute().EnsureSuccess();

        SelectQuery<Row> All() => new SelectQuery<Row>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC);
        async Task<List<Row>> Read() => (asynchronous ? await All().ExecuteAsync(session) : All().Execute(session)).GetResultOrThrow();

        // 1,100 rows need more than one command on every database, so the batches share an owned transaction on the connection.
        var many = Enumerable.Range(1, 1100).Select(i => new Row { Id = i, Label = "row" + i }).ToArray();
        var insert = new InsertQuery<Row>().InsertAuto(many);
        Check((asynchronous ? await insert.ExecuteAsync(session) : insert.Execute(session)).GetResultOrThrow() == 1100, "A batched insert did not report every row");
        Check((await Read()).Count == 1100, "The batched insert was not readable on the session connection");
        Check(new SelectQuery<Row>().SelectRootColumns().Execute(db).State == ResponseState.Error, "A pooled connection saw the session's temporary table");

        var exists = new SelectQuery<Row>().Where(x => x.Id, 1100, SqlOp.EQ);
        Check((asynchronous ? await exists.ExecuteExistsAsync(session) : exists.ExecuteExists(session)).GetResultOrThrow(), "The existence check did not use the session connection");
        var page = asynchronous ? await All().Page(2, 10).ExecuteWithTotalCountAsync(session) : All().Page(2, 10).ExecuteWithTotalCount(session);
        Check(page.EnsureSuccess().TotalResults == 1100 && page.Result[0].Id == 11, "A paged read with its count did not use the session connection");
        Check((session.GetServerTime().GetResultOrThrow() - DateTimeOffset.UtcNow).Duration() < TimeSpan.FromMinutes(5), "Server time was not read on the session connection");

        int streamed = 0;
        await foreach (var row in All().ExecuteAsyncStream(session)) streamed += row.Id > 0 ? 1 : 0;
        Check(streamed == 1100, "A stream did not read the session's rows");

        var update = new UpdateQuery<Row>().Update(x => x.Label, "changed").Where(x => x.Id, 2, SqlOp.LTE);
        Check((asynchronous ? await update.ExecuteAsync(session) : update.Execute(session)).GetResultOrThrow() == 2, "An update did not use the session connection");
        var delete = new DeleteQuery<Row>().Where(x => x.Id, 1000, SqlOp.GT);
        Check((asynchronous ? await delete.ExecuteAsync(session) : delete.Execute(session)).GetResultOrThrow() == 100, "A delete did not use the session connection");

        // The duplicate key is in the second command. The first command's rows must be rolled back with it,
        // which shows that the batches shared an owned transaction on the session connection.
        var batches = Enumerable.Range(5000, 1100).Select(i => new Row { Id = i == 6099 ? 1 : i, Label = "new" }).ToArray();
        var duplicate = new InsertQuery<Row>().InsertAuto(batches);
        var failed = asynchronous ? await duplicate.ExecuteAsync(session) : duplicate.Execute(session);
        Check(failed.State == ResponseState.Error, "A duplicate key was reported as success");
        var rows = await Read();
        Check(rows.Count == 1000 && rows[0].Label == "changed" && rows[2].Label == "row3" && rows.All(x => x.Id < 5000),
            "A failed batched write on the session connection was not rolled back as a whole");

        // A transaction passed to a terminal is used as given, and the caller decides its outcome.
        using (var transaction = connection.BeginTransaction())
        {
            new DeleteQuery<Row>().Where(x => x.Id, 0, SqlOp.GT).Execute(session, transaction).EnsureSuccess();
            Check(new SelectQuery<Row>().ExecuteExists(session, transaction).GetResultOrThrow() == false, "A read did not join the caller transaction");
            transaction.Rollback();
        }
        Check((await Read()).Count == 1000, "A caller transaction was committed");

        // Hand-written SQL follows the connector's rules on the session connection: Execute owns a transaction there.
        Check(session.BeginSql($"UPDATE {Q("#session_rows")} SET {Q("label")} = @label WHERE {Q("id")} = @id", new { label = "raw", id = 3 }).Execute().GetResultOrThrow() == 1,
            "Hand-written SQL did not use the session connection");
        Check(session.BeginSql($"SELECT {Q("label")} FROM {Q("#session_rows")} WHERE {Q("id")} = @id", new { id = 3 }).ExecuteList<string>().GetResultOrThrow().Single() == "raw",
            "A raw read did not use the session connection");

        if (sqlServer)
        {
            // Table type management runs on separate connections, while the insert itself stays on the session.
            var tvp = new InsertQuery<Row>().InsertAuto(new[] { new Row { Id = 3001, Label = "tvp" }, new Row { Id = 3002, Label = "tvp" } });
            Check((asynchronous ? await tvp.ExecuteTvpAsync(session) : tvp.ExecuteTvp(session)).GetResultOrThrow() == 2, "A table-valued insert did not use the session connection");
            Check((await Read()).Count(x => x.Label == "tvp") == 2, "The table-valued insert was not readable on the session connection");
        }

        Check(connection.State == ConnectionState.Open, "The session connection was closed");
        // A provider may return this session to its pool as it is, so the temporary table is removed first.
        session.BeginSql($"DROP {(db.DbFlavor == DbFlavor.MySql ? "TEMPORARY " : "")}TABLE {Q("#session_rows")}").WithoutTransaction().Execute().EnsureSuccess();
        connection.Close();
        var closed = asynchronous ? await All().ExecuteAsync(session) : All().Execute(session);
        Check(closed.State == ResponseState.Error && closed.Error.InnerException is InvalidOperationException, "A closed session connection did not return an error response");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    [SqlTable("#session_rows")]
    public sealed class Row
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("label")] public string Label { get; set; }
    }
}
