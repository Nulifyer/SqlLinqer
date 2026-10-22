using System.Data;
using Microsoft.Data.Sqlite;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using static SqlLinqer.Tests.Sqlite.CheckAssertions;

namespace SqlLinqer.Tests.Sqlite;

internal static class WriteChecks
{
    internal static void Insert(BaseConnector connector, Item item, string label, Action<string, Action> Test)
    {
        Test($"{label}: insert RETURNING converts Int64 primary keys to Int32", () =>
        {
            var response = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(item).Execute(connector);
            Expect(Result(response) == 1);
            Expect(response.TouchedPrimaryKeys.SequenceEqual([1]));
        });
        Test($"{label}: multirow insert and null/default values", () =>
        {
            var response = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new[] {
                new Item { Name = "second", CategoryId = 1, Bytes = [] },
                new Item { Name = "third", CategoryId = null, Bytes = null }
            }).Execute(connector);
            Expect(Result(response) == 2);
            Expect(response.TouchedPrimaryKeys.Order().SequenceEqual([2, 3]));
        });
    }

    internal static void UpdateAndDelete(BaseConnector connector, SqliteConnection keeper, Item item, string label, Action<string, Action> Test)
    {
        void Run(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        long Scalar(string sql) => Result(connector.ExecuteScalar<long>(new RenderedQuery(sql)));
        Test($"{label}: GUID primary keys and default-only inserts", () =>
        {
            Run("CREATE TABLE guid_rows (Id TEXT PRIMARY KEY, Name TEXT); CREATE TABLE defaults (Id INTEGER PRIMARY KEY)");
            Guid id = Guid.NewGuid();
            var inserted = new InsertWithPrimaryKeyQuery<GuidRow, Guid>().InsertAuto(new GuidRow { Id = id, Name = "guid" }).Execute(connector);
            Expect(Result(inserted) == 1 && inserted.TouchedPrimaryKeys.Single() == id);
            var updated = new UpdateWithPrimaryKeyQuery<GuidRow, Guid>().Update(x => x.Name, "updated").Where(x => x.Id, id, SqlOp.EQ).Execute(connector);
            Expect(Result(updated) == 1 && updated.TouchedPrimaryKeys.Single() == id);
            var deleted = new DeleteWithPrimaryKeyQuery<GuidRow, Guid>().Where(x => x.Id, id, SqlOp.EQ).Execute(connector);
            Expect(Result(deleted) == 1 && deleted.TouchedPrimaryKeys.Single() == id);
            var defaults = new InsertWithPrimaryKeyQuery<DefaultRow, int>().InsertAuto(new DefaultRow()).Execute(connector);
            Expect(Result(defaults) == 1 && defaults.TouchedPrimaryKeys.Single() == 1);
            Reject(() => new InsertQuery<DefaultRow>().InsertAuto(new[] { new DefaultRow(), new DefaultRow() }).Render(DbFlavor.Sqlite));
        });
        Test($"{label}: default expressions RETURNING and failed bulk writes are atomic", () =>
        {
            Run("CREATE TABLE generated_rows (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL DEFAULT 'generated', Number INTEGER NOT NULL DEFAULT (6*7))");
            var output = Result(new InsertQuery<GeneratedRow>().InsertAuto(new GeneratedRow())
                .Output(x => x.Id).Output(x => x.Name).Output(x => x.Number).ExecuteWithOutput(connector));
            Expect(output.Rows.Count == 1 && (long)output.Rows[0][0] == 1 && (string)output.Rows[0][1] == "generated" && (long)output.Rows[0][2] == 42);
            var key = new InsertWithPrimaryKeyQuery<GeneratedRow, long>().InsertAuto(new GeneratedRow()).Execute(connector);
            Expect(Result(key) == 1 && key.TouchedPrimaryKeys.Single() == 2L);
            Guid id = Guid.NewGuid();
            var failedBulk = new InsertWithPrimaryKeyQuery<GuidRow, Guid>().InsertAuto(new[] {
                new GuidRow { Id = id, Name = "first" }, new GuidRow { Id = id, Name = "duplicate" }
            }).Execute(connector);
            Expect(failedBulk.State == ResponseState.Error);
            Expect(Result(new SelectQuery<GuidRow>().SelectRootColumns().Where(x => x.Id, id, SqlOp.EQ).Execute(connector)).Count == 0);
            var retry = new InsertWithPrimaryKeyQuery<GuidRow, Guid>().InsertAuto(new GuidRow { Id = id, Name = "retry" }).Execute(connector);
            Expect(Result(retry) == 1 && retry.TouchedPrimaryKeys.Single() == id);
        });
        Test($"{label}: joined writes without a primary key are rejected", () =>
        {
            Reject(() => new UpdateQuery<NoKeyRow>().Update(x => x.Name, "bad").Where(x => x.Category.Name, "group", SqlOp.EQ).Render(DbFlavor.Sqlite));
            Reject(() => new DeleteQuery<NoKeyRow>().Where(x => x.Category.Name, "group", SqlOp.EQ).Render(DbFlavor.Sqlite));
        });
        Test($"{label}: plain updates use unqualified targets and commit owned transactions", () =>
        {
            Expect(Result(new UpdateQuery<Item>().Update(x => x.OtherName, x => x.Name).Where(x => x.Id, 2, SqlOp.EQ).Execute(connector)) == 1);
            Expect(Result(new SelectQuery<Item>().Select(x => x.OtherName).Where(x => x.Id, 2, SqlOp.EQ).Execute(connector)).Single().OtherName == "second");
            Expect(Result(new SelectPrimaryKeyExistsQuery<Item, int>(1).Execute(connector)));
            Expect(!Result(new SelectPrimaryKeyExistsQuery<Item, int>(-1).Execute(connector)));
        });
        Test($"{label}: update RETURNING with primary keys and arithmetic", () =>
        {
            var response = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, "changed").Where(x => x.Id, 1, SqlOp.EQ).Execute(connector);
            Expect(Result(response) == 1 && response.TouchedPrimaryKeys.Single() == 1);
            var output = Result(new UpdateQuery<Item>().Update(x => x.Id, new BasicMathOperation(BasicMathOperation.MathOp.Add, 0))
                .Output(x => x.Name).Where(x => x.Id, 1, SqlOp.EQ).ExecuteWithOutput(connector));
            Expect((string)output.Rows[0][0] == item.Name);
        });
        Test($"{label}: joined update uses safe target selection", () =>
        {
            var response = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, "joined")
                .Where(x => x.Category.Name, "group", SqlOp.EQ).Execute(connector);
            Expect(Result(response) == 2 && response.TouchedPrimaryKeys.Order().SequenceEqual([1, 2]));
            var cte = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, "child")
                .Where(x => x.Children.First().Name, "a", SqlOp.EQ).Execute(connector);
            Expect(Result(cte) == 1 && cte.TouchedPrimaryKeys.Single() == 1);
            var unmatched = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, "unmatched")
                .Where(x => x.Category.Name, (string)null, SqlOp.EQ).Execute(connector);
            Expect(Result(unmatched) == 1 && unmatched.TouchedPrimaryKeys.Single() == 3);
        });
        Test($"{label}: joined source assignments preserve LEFT nulls and simultaneous target references", () =>
        {
            using var transaction = keeper.BeginTransaction();
            var query = new UpdateWithPrimaryKeyQuery<Item, int>()
                .Update(x => x.OtherName, x => x.Name).Update(x => x.Name, x => x.Category.Name);
            string sql = query.Render(DbFlavor.Sqlite).Text;
            Expect(sql.Contains("LEFT JOIN") && !sql.Contains("LIMIT"));
            var updated = query.Execute(connector, transaction);
            Expect(Result(updated) == 3 && updated.TouchedPrimaryKeys.Order().SequenceEqual([1, 2, 3]));
            var rows = Result(new SelectQuery<Item>().Select(x => x.Id).Select(x => x.Name).Select(x => x.OtherName)
                .OrderBy(x => x.Id, SqlDir.ASC).Execute(connector, transaction));
            Expect(rows[0].Name == "group" && rows[0].OtherName == item.Name);
            Expect(rows[1].Name == "group" && rows[1].OtherName == "second");
            Expect(rows[2].Name == null && rows[2].OtherName == "third");
            transaction.Rollback();
            Expect(Result(new SelectQuery<Item>().Select(x => x.Name).Where(x => x.Id, 1, SqlOp.EQ).Execute(connector)).Single().Name == item.Name);
        });
        Test($"{label}: joined source assignments combine CTE filters RETURNING and no matches", () =>
        {
            using var transaction = keeper.BeginTransaction();
            var query = new UpdateQuery<Item>().Update(x => x.OtherName, x => x.Category.Name)
                .Where(x => x.Children.First().Name, "a", SqlOp.EQ).Output(x => x.Id).Output(x => x.OtherName);
            Expect(query.Render(DbFlavor.Sqlite).Text.Contains("WITH"));
            var output = Result(query.ExecuteWithOutput(connector, transaction));
            Expect(output.Rows.Count == 1 && (long)output.Rows[0][0] == 1 && (string)output.Rows[0][1] == "group");
            var none = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, x => x.Category.Name)
                .Where(x => x.Category.Name, "absent", SqlOp.EQ).Execute(connector, transaction);
            Expect(Result(none) == 0 && none.TouchedPrimaryKeys.Count == 0);
            var nulls = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, x => x.Category.Name)
                .Where(x => x.Category.Name, (string)null, SqlOp.EQ).Execute(connector, transaction);
            Expect(Result(nulls) == 1 && nulls.TouchedPrimaryKeys.Single() == 3);
            transaction.Rollback();
        });
        Test($"{label}: joined duplicate sources update each target once using a native matching value", () =>
        {
            using var transaction = keeper.BeginTransaction();
            var query = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, x => x.Children.First().Name)
                .Update(x => x.Id, new BasicMathOperation(BasicMathOperation.MathOp.Add, 10))
                .Where(x => x.Id, 1, SqlOp.EQ);
            Expect(!query.Render(DbFlavor.Sqlite).Text.Contains("LIMIT"));
            var updated = query.Execute(connector, transaction);
            Expect(Result(updated) == 1 && updated.TouchedPrimaryKeys.Single() == 11);
            var row = Result(new SelectQuery<Item>().Select(x => x.OtherName).Where(x => x.Id, 11, SqlOp.EQ).Execute(connector, transaction)).Single();
            Expect(new[] { "z", "a", "m" }.Contains(row.OtherName));
            transaction.Rollback();
            Expect(Scalar("SELECT count(*) FROM items WHERE Id = 1") == 1);
        });
        Test($"{label}: rejects unsupported requests before execution", () =>
        {
            try { new UpdateQuery<Item>().Update(x => x.Category.Name, "bad"); throw new Exception("Expected root-column validation"); }
            catch (FormatException) { }
            Reject(() => new TruncateQuery<Item>().Render(DbFlavor.Sqlite));
            Reject(() => new SqlLinqer.Tests.Regression.ProcedureProbe().Render(new SqlLinqer.Tests.Regression.ProcedureProbe.Input(), DbFlavor.Sqlite));
            Reject(() => new InsertQuery<Item>().InsertAuto(item).RenderTVP(new ParameterCollection(), DbFlavor.Sqlite, null));
            Reject(() => new SelectQueryInitializer<Item>().BuildQuery(q => new { Value = q.REVERSE(x => x.Name) }).Render(DbFlavor.Sqlite));
        });
        Test($"{label}: rollback and failed writes keep caller ownership", () =>
        {
            using var connection = (SqliteConnection)connector.CreateConnection(); connection.Open();
            using (var transaction = connection.BeginTransaction())
            {
                Result(new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item { Name = "rolled back" }).Execute(connector, transaction));
                Expect(connector.ExecuteNonQuery(new RenderedQuery("INSERT INTO no_such_table VALUES (1)"), transaction).State == ResponseState.Error);
                Expect(transaction.Connection == connection && connection.State == ConnectionState.Open);
                transaction.Rollback();
            }
            Expect(Scalar("SELECT count(*) FROM items") == 3);
            using (var transaction = connection.BeginTransaction())
            {
                Result(new InsertQuery<Item>().InsertAuto(new Item { Name = "committed" }).Execute(connector, transaction));
                transaction.Commit();
            }
            Expect(Scalar("SELECT count(*) FROM items") == 4);
        });
        Test($"{label}: delete RETURNING through joined filters", () =>
        {
            using (var transaction = keeper.BeginTransaction())
            {
                var cteDelete = new DeleteWithPrimaryKeyQuery<Item, int>().Where(x => x.Children.First().Name, "a", SqlOp.EQ).Execute(connector, transaction);
                Expect(Result(cteDelete) == 1 && cteDelete.TouchedPrimaryKeys.Single() == 1);
                transaction.Rollback();
            }
            var response = new DeleteWithPrimaryKeyQuery<Item, int>().Where(x => x.Category.Name, "group", SqlOp.EQ).Execute(connector);
            Expect(Result(response) == 2 && response.TouchedPrimaryKeys.Order().SequenceEqual([1, 2]));
            Expect(Scalar("SELECT count(*) FROM items") == 2);
            var output = Result(new DeleteQuery<Item>().Output(x => x.Name).Where(x => x.Id, 3, SqlOp.EQ).ExecuteWithOutput(connector));
            Expect((string)output.Rows[0][0] == "third");
        });
    }
}
