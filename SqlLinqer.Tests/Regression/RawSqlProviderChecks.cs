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
            test($"raw sql: named values, terminals, and a session connection, async={asynchronous}", () => CheckProvider(db, asynchronous).GetAwaiter().GetResult());
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
            using var connection = db.CreateConnection();
            connection.Open();
            string temp = sqlServer ? "#raw_sql_session" : Q("raw_sql_session");
            var create = db.BeginSql(sqlServer ? $"CREATE TABLE {temp} (Id INT)" : $"CREATE TEMPORARY TABLE {temp} (Id INT)");
            (asynchronous ? await create.ExecuteAsync(connection) : create.Execute(connection)).EnsureSuccess();
            var add = db.BeginSql($"INSERT INTO {temp} (Id) VALUES (@a), (@b)", new { a = 4, b = 5 });
            (asynchronous ? await add.ExecuteAsync(connection) : add.Execute(connection)).EnsureSuccess();
            var total = db.BeginSql($"SELECT SUM(Id) FROM {temp} WHERE Id > @floor", new { floor = 0 });
            Check((asynchronous ? await total.ExecuteScalarAsync<long>(connection) : total.ExecuteScalar<long>(connection)).GetResultOrThrow() == 9,
                "The session read used another connection");
            var ids = db.BeginSql($"SELECT Id FROM {temp} ORDER BY Id");
            var session = (asynchronous ? await ids.ExecuteListAsync<int>(connection) : ids.ExecuteList<int>(connection)).GetResultOrThrow();
            Check(session.SequenceEqual(new[] { 4, 5 }), "The session list used another connection");
            Check(connection.State == ConnectionState.Open, "The supplied connection was closed");

            using var transaction = connection.BeginTransaction();
            db.BeginSql($"DELETE FROM {table} WHERE {Q("Id")} = @id", new { id = 2 }).Execute(transaction).EnsureSuccess();
            transaction.Rollback();
            Check(db.BeginSql($"SELECT COUNT(*) FROM {table}").ExecuteScalar<long>().GetResultOrThrow() == 2, "A caller transaction was committed");
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
