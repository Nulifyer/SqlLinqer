using SqlLinqer.Components.Update;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

// Reads and assigns the database server's time on every live provider.
internal static class ServerTimeProviderChecks
{
    internal static void Run(BaseConnector db, Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
            test($"server time: read as UTC and assigned by updates and conflicts, async={asynchronous}", () => CheckProvider(db, asynchronous).GetAwaiter().GetResult());
    }

    private static async Task CheckProvider(BaseConnector db, bool asynchronous)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string table = (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : Q(db.Options.DefaultSchema) + ".") + Q("server_time_rows");
        string type = db.DbFlavor switch
        {
            DbFlavor.PostgreSql or DbFlavor.DuckDb => "TIMESTAMPTZ",
            DbFlavor.MySql => "DATETIME(6)",
            DbFlavor.Sqlite => "TEXT",
            _ => "DATETIME2",
        };
        void Near(DateTimeOffset value, string message)
            => Check(value.Offset == TimeSpan.Zero && (value - DateTimeOffset.UtcNow).Duration() < TimeSpan.FromMinutes(5), $"{message}: {value:o}");
        DateTimeOffset Utc(DateTime value)
            => new(DateTime.SpecifyKind(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value, DateTimeKind.Utc));
        try
        {
            Near((asynchronous ? await db.GetServerTimeAsync() : db.GetServerTime()).GetResultOrThrow(), "Server time is not the current UTC instant");
            using (var connection = db.CreateConnection())
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();
                Near(db.GetServerTime(transaction).GetResultOrThrow(), "Server time differs inside a caller transaction");
                transaction.Rollback();
            }

            db.BeginSql($"CREATE TABLE {table} ({Q("id")} INTEGER PRIMARY KEY, {Q("touched")} {type} NULL)").Execute().EnsureSuccess();
            db.BeginInsert<Row>().InsertAuto(new Row { Id = 1 }).Execute().EnsureSuccess();
            Row Read() => db.BeginSelect<Row>().SelectRootColumns().Where(x => x.Id, 1, SqlOp.EQ).Execute().GetResultOrThrow().Single();

            var update = db.BeginUpdate<Row>().Update(x => x.Touched, new ServerTimeOperation()).Where(x => x.Id, 1, SqlOp.EQ);
            Check((asynchronous ? await update.ExecuteAsync() : update.Execute()).GetResultOrThrow() == 1, "The update did not affect the row");
            Near(Utc(Read().Touched.Value), "An update did not assign the server time");

            // A server-assigned time must sort and compare with times the application binds.
            int Matching(DateTime bound, SqlOp op) => db.BeginSelect<Row>().SelectRootColumns().Where(x => x.Touched, (DateTime?)bound, op).Execute().GetResultOrThrow().Count;
            var reference = DateTime.SpecifyKind(DateTime.UtcNow, db.DbFlavor is DbFlavor.PostgreSql or DbFlavor.DuckDb ? DateTimeKind.Utc : DateTimeKind.Unspecified);
            Check(Matching(reference.AddMinutes(10), SqlOp.LT) == 1 && Matching(reference.AddMinutes(-10), SqlOp.GT) == 1,
                "A server-assigned time did not fall between bound times around now");
            Check(Matching(reference.AddMinutes(10), SqlOp.GT) == 0 && Matching(reference.AddMinutes(-10), SqlOp.LT) == 0,
                "A server-assigned time compared wrongly with a bound time");

            db.BeginUpdate<Row>().Update(x => x.Touched, (DateTime?)null).Where(x => x.Id, 1, SqlOp.EQ).Execute().EnsureSuccess();
            db.BeginInsert<Row>().InsertAuto(new Row { Id = 1 }).OnConflict(x => x.Id).OnConflictUpdate(x => x.Touched, new ServerTimeOperation())
                .Execute().EnsureSuccess();
            Near(Utc(Read().Touched.Value), "A conflict update did not assign the server time");
        }
        finally { db.BeginSql($"DROP TABLE IF EXISTS {table}").Execute(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    [SqlTable("server_time_rows")]
    public sealed class Row
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("touched")] public DateTime? Touched { get; set; }
    }
}
