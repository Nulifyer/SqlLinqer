using System.Data;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using static SqlLinqer.Tests.DuckDb.CheckAssertions;

namespace SqlLinqer.Tests.DuckDb;

internal static class WriteChecks
{
    internal static void Update(BaseConnector connector, Action<string, Action> Test)
    {
        void Sql(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        long Scalar(string sql) => Result(connector.ExecuteScalar<long>(new RenderedQuery(sql)));
        Test("update returned rows, arithmetic, columns and affected counts", () =>
        {
            using var table = Result(new UpdateQuery<Person>().Update(x => x.Name, "changed").Where(x => x.Id,101,SqlOp.EQ).Output(x => x.Name).ExecuteWithOutput(connector));
            Expect((string)table.Rows[0][0] == "changed", "Incorrect returned update");
            var keys = new UpdateWithPrimaryKeyQuery<Person,int>().Update(x=>x.Note,"key").Where(x=>x.Id,101,SqlOp.EQ).Execute(connector);
            Expect(Result(keys) == 1 && keys.TouchedPrimaryKeys.Single() == 101,"Updated key wrong");
            Expect(Result(new UpdateQuery<Person>().Update(x => x.Note, x => x.Name).Where(x => x.Id,101,SqlOp.EQ).Execute(connector)) == 1, "Column update count wrong");
            Expect(Result(new UpdateQuery<Person>().Update(x => x.Amount, new SqlLinqer.Components.Update.BasicMathOperation(SqlLinqer.Components.Update.BasicMathOperation.MathOp.Add, 1m)).Where(x => x.Id,100,SqlOp.EQ).Execute(connector)) == 1, "Arithmetic update failed");
            var subquery = new SelectQuery<Person>().Select(x=>x.Id).Where(x=>x.Name,"changed",SqlOp.EQ);
            Expect(Result(new UpdateQuery<Person>().Update(x=>x.Note,"changed").Where(x=>x.Id,subquery,SqlArrayOp.IN).Execute(connector)) == 1,"Subquery write failed");
            using var empty = Result(new UpdateQuery<Person>().Update(x=>x.Note,"absent").Where(x=>x.Id,-1,SqlOp.EQ).Output(x=>x.Id).ExecuteWithOutput(connector));
            Expect(empty.Rows.Count == 0,"Empty returned rows failed");
            Expect(Result(new UpdateQuery<Person>().Update(x => x.Note, "none").Where(x => x.Id,-1,SqlOp.EQ).Execute(connector)) == 0, "Missing update count wrong");
        });
        Test("caller owns transaction, reads/writes/count/output and rollback", () =>
        {
            using var connection = connector.CreateConnection(); connection.Open();
            using var transaction = connection.BeginTransaction(IsolationLevel.Snapshot);
            var added = new InsertWithPrimaryKeyQuery<Person,int>().InsertAuto(new Person { Name = "rollback" }).Execute(connector, transaction);
            Expect(Result(added) == 1, "Insert in transaction failed");
            int id = added.TouchedPrimaryKeys.Single();
            var page = new SelectQuery<Person>().SelectRootColumns().Where(x => x.Id,id,SqlOp.EQ).Top(1).ExecuteWithTotalCount(connector,transaction);
            Expect(Result(page).Count == 1 && page.TotalResults == 1, "Read/count ignored caller transaction");
            Expect(Result(new UpdateQuery<Person>().Update(x=>x.Note,"tx").Where(x=>x.Id,id,SqlOp.EQ).Execute(connector,transaction)) == 1, "Update ignored transaction");
            Expect(Result(new DeleteWithPrimaryKeyQuery<Person,int>().Where(x=>x.Id,101,SqlOp.EQ).Execute(connector,transaction)) == 1, "Delete ignored transaction");
            Expect(Result(new UpdateQuery<Person>().Update(x=>x.Note,"rollback").Where(x=>x.Id,100,SqlOp.EQ).Execute(connector,transaction)) == 1,"Existing row update failed");
            Expect(connection.State == ConnectionState.Open && transaction.Connection == connection, "Connector completed caller transaction");
            transaction.Rollback();
            Expect(Scalar($"SELECT count(*) FROM people WHERE Id={id}") == 0 && Scalar("SELECT count(*) FROM people") == 4 && Scalar("SELECT count(*) FROM people WHERE Id=101") == 1 && Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL") == 1, "Rollback failed");
        });
        Test("owned transaction rolls back earlier statements on provider failure", () =>
        {
            var failure = connector.ExecuteNonQuery(new RenderedQuery("UPDATE people SET Note='must rollback' WHERE Id=100; INSERT INTO nonexistent_table VALUES (1)"));
            Expect(failure.State == ResponseState.Error && Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL") == 1, "Owned failure was not rolled back");
        });
        Test("joined source assignments, outputs, keys and LEFT JOIN nulls roll back", () =>
        {
            using var connection = connector.CreateConnection(); connection.Open();
            using var tx = connection.BeginTransaction(IsolationLevel.Snapshot);
            var query = new UpdateQuery<Person>().Update(x=>x.Note,x=>x.Category.Name).Update(x=>x.CategoryId,x=>x.Category.Id).Output(x=>x.Id).Output(x=>x.Note).Output(x=>x.CategoryId);
            var first = query.Render(DbFlavor.DuckDb);
            Expect(query.Render(DbFlavor.SqlServer).Text.Contains("UPDATE"), "Cross-dialect update failed");
            Expect(query.Render(DbFlavor.DuckDb).Text == first.Text, "Joined update render mutated query");
            using var rows = Result(query.ExecuteWithOutput(connector,tx));
            Expect(rows.Rows.Count == 4 && rows.Rows.Cast<DataRow>().Single(x=>(int)x["Id"]==100)["Note"].Equals("group") && rows.Rows.Cast<DataRow>().Where(x=>(int)x["Id"]!=100).All(x=>x.IsNull("Note") && x.IsNull("CategoryId")), "Joined source or LEFT JOIN changed values");
            var keys = new UpdateWithPrimaryKeyQuery<Person,long>().Update(x=>x.Note,"joined").Where(x=>x.Category.Name,"group",SqlOp.EQ).Execute(connector,tx);
            Expect(Result(keys)==1 && keys.TouchedPrimaryKeys.Single()==100L, "Joined update keys failed");
            Expect(Result(new UpdateQuery<Person>().Update(x=>x.Note,"absent").Where(x=>x.Category.Name,"missing",SqlOp.EQ).Execute(connector,tx))==0, "Unmatched update changed data");
            var deleted = new DeleteWithPrimaryKeyQuery<Person,long>().Where(x=>x.Category.Name,(string)null,SqlOp.EQ).Execute(connector,tx);
            Expect(Result(deleted)==3 && deleted.TouchedPrimaryKeys.Order().SequenceEqual(new long[]{101,102,103}), "LEFT JOIN delete keys failed");
            tx.Rollback();
            Expect(Scalar("SELECT count(*) FROM people")==4 && Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL")==1, "Joined writes escaped transaction");
        });
        Test("joined writes combine related-row CTE filters and preserve no-match output", () =>
        {
            using var connection = connector.CreateConnection(); connection.Open();
            using var tx = connection.BeginTransaction(IsolationLevel.Snapshot);
            var update = new UpdateWithPrimaryKeyQuery<Person,int>().Update(x=>x.Note,x=>x.Category.Name).Where(x=>x.Children.First().Name,"a",SqlOp.EQ);
            Expect(update.Render(DbFlavor.DuckDb).Text.TrimStart().StartsWith("WITH"), "Expected related filter CTE");
            var changed = update.Execute(connector,tx);
            Expect(Result(changed)==1 && changed.TouchedPrimaryKeys.Single()==100,"Related CTE update failed");
            using var deleted = Result(new DeleteQuery<Person>().Where(x=>x.Category.Name,"group",SqlOp.EQ).Where(x=>x.Children.First().Name,"a",SqlOp.EQ).Output(x=>x.Id).ExecuteWithOutput(connector,tx));
            Expect(deleted.Rows.Count==1 && (int)deleted.Rows[0][0]==100,"Related CTE joined delete failed");
            using var absent = Result(new DeleteQuery<Person>().Where(x=>x.Category.Name,"absent",SqlOp.EQ).Output(x=>x.Id).ExecuteWithOutput(connector,tx));
            Expect(absent.Rows.Count==0,"Unmatched delete returned rows");
            tx.Rollback();
        });
        Test("duplicate source matches reject ambiguous updates and delete each target once", () =>
        {
            using var connection = connector.CreateConnection(); connection.Open();
            using(var tx = connection.BeginTransaction(IsolationLevel.Snapshot))
            {
                Result(connector.ExecuteNonQuery(new RenderedQuery("INSERT INTO categories VALUES (1,'other')"),tx));
                var failure = new UpdateWithPrimaryKeyQuery<Person,int>().Update(x=>x.Note,x=>x.Category.Name).Where(x=>x.Id,100,SqlOp.EQ).Execute(connector,tx);
                Expect(failure.State==ResponseState.Error && failure.Error.ToString().Contains("at most one source match"),"Ambiguous update did not report precise error");
                tx.Rollback();
            }
            Expect(Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL")==1,"Ambiguous update escaped rollback");
            using(var tx = connection.BeginTransaction(IsolationLevel.Snapshot))
            {
                Result(connector.ExecuteNonQuery(new RenderedQuery("INSERT INTO categories VALUES (1,'other')"),tx));
                var deleted = new DeleteWithPrimaryKeyQuery<Person,int>().Where(x=>x.Category.Name,new[]{"group","other"},SqlArrayOp.IN).Execute(connector,tx);
                Expect(Result(deleted)==1 && deleted.TouchedPrimaryKeys.Single()==100,"Duplicate delete did not delete target once");
                tx.Rollback();
            }
        });
        Test("owned joined writes persist", () =>
        {
            Sql("CREATE TABLE write_people (Id INTEGER PRIMARY KEY, CategoryId INTEGER, Note VARCHAR); INSERT INTO write_people VALUES(1,1,NULL),(2,99,NULL)");
            Expect(Result(new UpdateQuery<WritePerson>().Update(x=>x.Note,x=>x.Category.Name).Execute(connector))==2,"Owned joined update failed");
            Expect(Scalar("SELECT count(*) FROM write_people WHERE Note='group'")==1,"Owned joined update did not persist");
            Expect(Result(new DeleteQuery<WritePerson>().Where(x=>x.Category.Name,"group",SqlOp.EQ).Execute(connector))==1,"Owned joined delete failed");
        });
    }

    internal static void Defaults(BaseConnector connector, Action<string, Action> Test)
    {
        void Sql(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        Test("default-only batch inserts and integer long GUID key conversions", () =>
        {
            Sql("CREATE SEQUENCE default_ids START 2147483648; CREATE TABLE default_keys (Id BIGINT PRIMARY KEY DEFAULT nextval('default_ids')); CREATE TABLE guid_keys (Id UUID PRIMARY KEY DEFAULT uuid())");
            var batch = new InsertWithPrimaryKeyQuery<DefaultKey,long>().InsertAuto(new[]{new DefaultKey(),new DefaultKey()}).Execute(connector);
            Expect(Result(batch)==2 && batch.TouchedPrimaryKeys.Order().SequenceEqual(new long[]{2147483648,2147483649}),"Default batch or BIGINT keys failed");
            var widened=new UpdateWithPrimaryKeyQuery<Person,long>().Update(x=>x.Note,"key conversion").Where(x=>x.Id,-1,SqlOp.EQ).Execute(connector);
            Expect(Result(widened)==0 && widened.TouchedPrimaryKeys.Count==0,"Empty key conversion failed");
            var guid=new InsertWithPrimaryKeyQuery<GuidKey,Guid>().InsertAuto(new GuidKey()).Execute(connector);
            Expect(Result(guid)==1 && guid.TouchedPrimaryKeys.Single()!=Guid.Empty,"Default UUID key failed");
            var key=guid.TouchedPrimaryKeys.Single();
            var deleted=new DeleteWithPrimaryKeyQuery<GuidKey,Guid>().Where(x=>x.Id,key,SqlOp.EQ).Execute(connector);
            Expect(Result(deleted)==1 && deleted.TouchedPrimaryKeys.Single()==key,"UUID delete key failed");
            Expect(Result(connector.ExecuteScalar<byte[]>(new RenderedQuery("SELECT from_hex('00FF')"))).SequenceEqual(new byte[]{0,255}),"Scalar BLOB was not copied before reader disposal");
            Expect(Result(connector.ExecuteScalar<int?>(new RenderedQuery("SELECT NULL::INTEGER")))==null && Result(connector.ExecuteScalar<long?>(new RenderedQuery("SELECT 42::INTEGER")))==42,"Nullable scalar failed");
        });
    }

    internal static void Delete(BaseConnector connector, Action<string, Action> Test)
    {
        Test("delete keys/rows and affected counts", () =>
        {
            var deleted = new DeleteWithPrimaryKeyQuery<Person,int>().Where(x=>x.Id,103,SqlOp.EQ).Execute(connector);
            Expect(Result(deleted) == 1 && deleted.TouchedPrimaryKeys.Single() == 103, "Deleted key wrong");
            Expect(Result(new DeleteQuery<Person>().Where(x=>x.Id,102,SqlOp.EQ).Execute(connector)) == 1, "Delete count wrong");
            Expect(Result(new DeleteQuery<Person>().Where(x=>x.Id,-1,SqlOp.EQ).Execute(connector)) == 0, "Empty delete wrong");
        });
    }
}
