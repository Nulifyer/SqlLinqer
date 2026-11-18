using System.Data;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Tests.Regression;

// Runs hand-written SQL through BeginSql on every live provider.
internal static class RawSqlProviderChecks
{
    internal static void Run(BaseConnector db, Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
            test($"raw sql: named values, terminals, and a session connector, async={asynchronous}", () => CheckProvider(db, asynchronous).GetAwaiter().GetResult());
    }

    private static async Task CheckProvider(BaseConnector db, bool asynchronous)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string table = (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : Q(db.Options.DefaultSchema) + ".") + Q("raw_sql_rows");
        bool sqlServer = db.DbFlavor is DbFlavor.SqlServer or DbFlavor.AzureSql;
        try
        {
            db.BeginSql($"CREATE TABLE {table} ({Q("Id")} INTEGER PRIMARY KEY, {Q("Label")} VARCHAR(40), {Q("Note")} VARCHAR(40))").Execute().EnsureSuccess();
            var insert = db.BeginSql($"INSERT INTO {table} ({Q("Id")}, {Q("Label")}, {Q("Note")}) VALUES (@id, @label, @note)");
            int inserted = (asynchronous
                ? await insert.Values(new { id = 1, label = "a@b.c" }).Value("note", null, DbType.String).ExecuteAsync()
                : insert.Values(new { id = 1, label = "a@b.c" }).Value("note", null, DbType.String).Execute()).GetResultOrThrow();
            Check(inserted == 1, "Insert count changed");
            db.BeginSql($"INSERT INTO {table} ({Q("Id")}, {Q("Label")}, {Q("Note")}) VALUES (@id, @label, @label)", new { id = 2, label = "second" })
                .Execute().EnsureSuccess();

            var count = db.BeginSql($"SELECT COUNT(*) FROM {table} WHERE {Q("Id")} >= @low AND {Q("Label")} <> '@low'", new { low = 1 }).WithoutTransaction();
            Check((asynchronous ? await count.ExecuteScalarAsync<long>() : count.ExecuteScalar<long>()).GetResultOrThrow() == 2, "Scalar read changed");

            var list = db.BeginSql($"SELECT {Q("Id")}, {Q("Label")}, {Q("Note")} FROM {table} WHERE {Q("Id")} IN (@first, @second) ORDER BY {Q("Id")}",
                new { first = 1, second = 2 });
            var rows = (asynchronous ? await list.ExecuteListAsync<Row>() : list.ExecuteList<Row>()).GetResultOrThrow();
            Check(rows.Count == 2 && rows[0].Label == "a@b.c" && rows[0].Note == null && rows[1].Note == "second", "Rows were not mapped by column name");
            using (var data = (asynchronous ? await list.ExecuteDataTableAsync() : list.ExecuteDataTable()).GetResultOrThrow())
                Check(data.Rows.Count == 2, "DataTable read changed");

            // A temporary table exists only in its session, so these reads prove the commands share the supplied connection.
            using var connection = db.OpenConnection();
            var session = db.OnConnection(connection);
            string temp = sqlServer ? "#raw_sql_session" : Q("raw_sql_session");
            var create = session.BeginSql(sqlServer ? $"CREATE TABLE {temp} (Id INT)" : $"CREATE TEMPORARY TABLE {temp} (Id INT)").WithoutTransaction();
            (asynchronous ? await create.ExecuteAsync() : create.Execute()).EnsureSuccess();
            var add = session.BeginSql($"INSERT INTO {temp} (Id) VALUES (@a), (@b)", new { a = 4, b = 5 });
            (asynchronous ? await add.ExecuteAsync() : add.Execute()).EnsureSuccess();
            var total = session.BeginSql($"SELECT SUM(Id) FROM {temp} WHERE Id > @floor", new { floor = 0 }).WithoutTransaction();
            Check((asynchronous ? await total.ExecuteScalarAsync<long>() : total.ExecuteScalar<long>()).GetResultOrThrow() == 9,
                "The session read used another connection");
            var ids = session.BeginSql($"SELECT Id FROM {temp} ORDER BY Id");
            var sessionRows = (asynchronous ? await ids.ExecuteListAsync<int>() : ids.ExecuteList<int>()).GetResultOrThrow();
            Check(sessionRows.SequenceEqual(new[] { 4, 5 }), "The session list used another connection");
            Check(connection.State == ConnectionState.Open, "The session connection was closed");

            using var transaction = connection.BeginTransaction();
            db.BeginSql($"DELETE FROM {table} WHERE {Q("Id")} = @id", new { id = 2 }).Execute(transaction).EnsureSuccess();
            transaction.Rollback();
            Check(db.BeginSql($"SELECT COUNT(*) FROM {table}").ExecuteScalar<long>().GetResultOrThrow() == 2, "A caller transaction was committed");

            // A list value renders one parameter for each element.
            var listed = db.BeginSql($"SELECT {Q("Id")} FROM {table} WHERE {Q("Id")} IN (@ids) AND {Q("Label")} <> @other ORDER BY {Q("Id")}", new { other = "none" })
                .ValueList("ids", new[] { 2, 1, 7 });
            Check((asynchronous ? await listed.ExecuteListAsync<int>() : listed.ExecuteList<int>()).GetResultOrThrow().SequenceEqual(new[] { 1, 2 }),
                "A list value did not expand into its elements");
            var labels = db.BeginSql($"SELECT COUNT(*) FROM {table} WHERE {Q("Label")} NOT IN (@labels)").ValueList("labels", new List<string> { "second" }, DbType.String);
            Check(labels.WithoutTransaction().ExecuteScalar<long>().GetResultOrThrow() == 1, "A list of one typed element changed");

            // Rows stream one at a time, mapped as ExecuteList maps them, on a pooled connection, a transaction, or a supplied connection.
            var stream = db.BeginSql($"SELECT {Q("Id")}, {Q("Label")}, {Q("Note")} FROM {table} WHERE {Q("Id")} >= @low ORDER BY {Q("Id")}", new { low = 1 });
            var streamed = new List<Row>();
            await foreach (var row in stream.ExecuteAsyncStream<Row>()) streamed.Add(row);
            Check(streamed.Count == 2 && streamed[0].Label == "a@b.c" && streamed[0].Note == null && streamed[1].Note == "second", "Streamed rows were not mapped by column name");
            var scalars = new List<int>();
            await foreach (int id in session.BeginSql($"SELECT Id FROM {temp} ORDER BY Id").ExecuteAsyncStream<int>()) scalars.Add(id);
            Check(scalars.SequenceEqual(new[] { 4, 5 }) && connection.State == ConnectionState.Open, "A stream on a session connection used another connection or closed it");
            using (var streamTransaction = connection.BeginTransaction())
            {
                db.BeginSql($"DELETE FROM {table} WHERE {Q("Id")} = @id", new { id = 2 }).Execute(streamTransaction).EnsureSuccess();
                int seen = 0;
                await foreach (var row in stream.ExecuteAsyncStream<Row>(streamTransaction)) seen++;
                Check(seen == 1, "A stream did not read inside the caller transaction");
                streamTransaction.Rollback();
            }
            // Stopping early releases the command, so the next command can run on the same connection.
            await foreach (int id in session.BeginSql($"SELECT Id FROM {temp} ORDER BY Id").ExecuteAsyncStream<int>()) break;
            Check(session.BeginSql($"SELECT COUNT(*) FROM {temp}").ExecuteScalar<long>().GetResultOrThrow() == 2, "A stream that stopped early left its connection busy");
            bool mappingFailed = false;
            try { await foreach (var row in db.BeginSql($"SELECT {Q("Id")} FROM {table}").ExecuteAsyncStream<Row>()) { } }
            catch (SqlLinqer.Exceptions.SqlResponseException) { mappingFailed = true; }
            Check(mappingFailed, "A streamed row that could not be mapped did not throw SqlResponseException");

            // Several result sets map into one collection each, in order.
            var several = db.BeginSql($"SELECT {Q("Id")}, {Q("Label")}, {Q("Note")} FROM {table} WHERE {Q("Id")} = @id; SELECT {Q("Label")} FROM {table} ORDER BY {Q("Id")}", new { id = 2 });
            var (matches, allLabels) = (asynchronous
                ? await several.ExecuteListsAsync<(List<Row>, string[])>()
                : several.ExecuteLists<(List<Row>, string[])>()).GetResultOrThrow();
            Check(matches.Single().Note == "second" && allLabels.SequenceEqual(new[] { "a@b.c", "second" }), "Several result sets were not mapped in order");
            var onConnection = session.BeginSql($"SELECT Id FROM {temp} ORDER BY Id; SELECT COUNT(*) FROM {temp}");
            var (sessionIds, sessionCount) = (asynchronous
                ? await onConnection.ExecuteListsAsync<(List<int>, List<long>)>()
                : onConnection.ExecuteLists<(List<int>, List<long>)>()).GetResultOrThrow();
            Check(sessionIds.SequenceEqual(new[] { 4, 5 }) && sessionCount.Single() == 2, "Several result sets did not use the session connection");
            var tooFew = db.BeginSql($"SELECT {Q("Id")} FROM {table}").ExecuteLists<(List<int>, List<int>)>();
            Check(tooFew.State == ResponseState.Error && tooFew.Error.Message.Contains("1 result sets; expected 2"), "A missing result set was not reported");
        }
        finally { db.BeginSql($"DROP TABLE IF EXISTS {table}").Execute(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private sealed class Row
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Note { get; set; }
    }
}
