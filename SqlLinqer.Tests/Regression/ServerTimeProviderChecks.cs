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

            var update = db.BeginUpdate<Row>().Update(x => x.Touched, SqlTime.NOW).Where(x => x.Id, 1, SqlOp.EQ);
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
            db.BeginInsert<Row>().InsertAuto(new Row { Id = 1 }).OnConflict(x => x.Id).OnConflictUpdate(x => x.Touched, SqlTime.NOW)
                .Execute().EnsureSuccess();
            Near(Utc(Read().Touched.Value), "A conflict update did not assign the server time");

            // An insert assigns the server time to the named columns of every row, whatever the rows hold.
            var stale = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var insert = db.BeginInsert<Row>().InsertAuto(new[] { new Row { Id = 2, Touched = stale }, new Row { Id = 3 } }).Insert(x => x.Touched, SqlTime.NOW);
            Check((asynchronous ? await insert.ExecuteAsync() : insert.Execute()).GetResultOrThrow() == 2, "An insert with a server time did not insert its rows");
            var inserted = db.BeginSelect<Row>().SelectRootColumns().Where(x => x.Id, 1, SqlOp.GT).Execute().GetResultOrThrow();
            Check(inserted.Count == 2, "Inserted rows are missing");
            foreach (var row in inserted) Near(Utc(row.Touched.Value), "An insert did not assign the server time");

            // A conflict update can copy the inserted server time, and a new row keeps it.
            db.BeginUpdate<Row>().Update(x => x.Touched, (DateTime?)null).Where(x => x.Id, 2, SqlOp.EQ).Execute().EnsureSuccess();
            db.BeginInsert<Row>().InsertAuto(new[] { new Row { Id = 2 }, new Row { Id = 4 } }).Insert(x => x.Touched, SqlTime.NOW)
                .OnConflict(x => x.Id).OnConflictUpdate(x => x.Touched).Execute().EnsureSuccess();
            foreach (int id in new[] { 2, 4 })
                Near(Utc(db.BeginSelect<Row>().SelectRootColumns().Where(x => x.Id, id, SqlOp.EQ).Execute().GetResultOrThrow().Single().Touched.Value),
                    "An upsert did not assign the inserted server time");

            // A filter compares a column with the server's clock, without binding an application time.
            db.BeginInsert<Row>().InsertAuto(new[]
            {
                new Row { Id = 10, Touched = reference.AddHours(-2) }, new Row { Id = 11, Touched = reference.AddHours(2) }, new Row { Id = 12 },
            }).Execute().EnsureSuccess();
            List<int> Ids(SqlOp op) => db.BeginSelect<Row>().Select(x => x.Id).Where(x => x.Id, 10, SqlOp.GTE).Where(x => x.Touched, SqlTime.NOW, op)
                .OrderBy(x => x.Id, SqlDir.ASC).Execute().GetResultOrThrow().Select(x => x.Id).ToList();
            Check(Ids(SqlOp.LT).SequenceEqual(new[] { 10 }) && Ids(SqlOp.GT).SequenceEqual(new[] { 11 }),
                $"A comparison with the server time changed: before {string.Join(",", Ids(SqlOp.LT))}, after {string.Join(",", Ids(SqlOp.GT))}");
            var due = db.BeginSelect<Row>().Select(x => x.Id).Where(x => x.Id, 10, SqlOp.GTE).Where(x => x.Touched, SqlTime.NOW, SqlOp.LTE);
            Check(due.Render(db).Parameters.Count == 1, "A server-time comparison bound an application value");
            Check(db.BeginUpdate<Row>().Update(x => x.Touched, SqlTime.NOW).Where(x => x.Touched, SqlTime.NOW, SqlOp.GT).Execute().GetResultOrThrow() == 1,
                "An update filtered by the server time did not change the one future row");
            Check(db.BeginDelete<Row>().Where(x => x.Id, 10, SqlOp.GTE).Where(x => x.Touched, SqlTime.NOW, SqlOp.LTE).Execute().GetResultOrThrow() == 2,
                "A delete filtered by the server time did not remove the two past rows");
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
