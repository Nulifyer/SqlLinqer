using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Update;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using SqlLinqer.RegressionTests;

if (args.Contains("--benchmark-render-only"))
{
    CompatibleQueryRegressionTests.MeasureRendering();
    return 0;
}

int passed = 0, failed = 0;
void Test(string name, Action run)
{
    try { run(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
void Contains(string text, string expected) => Expect(text.Contains(expected), $"Expected '{expected}' in {text}");
void Excludes(string text, string unexpected) => Expect(!text.Contains(unexpected), $"Unexpected '{unexpected}' in {text}");
void Throws<T>(Action run) where T : Exception
{
    try { run(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
string Sql(SqlLinqer.Queries.Core.Abstract.BaseQuery query, DbFlavor flavor)
    => Regex.Replace(query.Render(flavor).Text, @"\s+", " ").Trim();

foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
{
    Test($"paging survives count rendering for {flavor}", () =>
    {
        var query = new SelectQuery<Item>().SelectRootColumns().Page(3, 10);
        string original = query.Render(flavor).Text;
        query.RenderForCount(new ParameterCollection(), flavor);
        Expect(query.Render(flavor).Text == original, "Counting changed the original query");
    });
    Test($"Top counts all matching rows for {flavor}", () =>
    {
        var database = new FakeDatabase();
        // Use row-by-row JSON while testing each dialect's paging behavior.
        var connector = new RowJsonConnector(database, flavor);
        var result = new SelectQuery<Item>().SelectRootColumns().Top(10).ExecuteWithTotalCount(connector);
        Expect(result.State == ResponseState.Valid, result.Error?.ToString());
        Expect(result.TotalResults == 22, "Top returned page length as total");
        Expect(database.Executed.Count == 2, "Count query was skipped");
    });
    Test($"Boolean parameters retain type for {flavor}", () =>
    {
        var rendered = new SelectQuery<Item>().SelectRootColumns().Where(x => x.Enabled, true, SqlOp.EQ).Render(flavor);
        Expect(rendered.Parameters.GetAll().Single().Value is true, "Boolean was converted to an integer");
    });
    Test($"empty IN stays false in a write for {flavor}", () =>
    {
        string sql = Sql(new DeleteQuery<Item>().Where(x => x.Id, Array.Empty<int>(), SqlArrayOp.IN), flavor);
        Contains(sql, "WHERE 1 = 0");
        Excludes(sql, "IN ()");
    });
    Test($"empty NOT IN stays true inside OR for {flavor}", () =>
    {
        var query = new SelectQuery<Item>().SelectRootColumns();
        var group = query.NewWhere(SqlWhereOp.OR)
            .Where(x => x.Id, Array.Empty<int>(), SqlArrayOp.NOTIN)
            .Where(x => x.Name, "sample", SqlOp.EQ);
        Contains(Sql(query.Where(group), flavor), "1 = 1 OR");
    });
}

Test("rendering Top across dialects preserves SQL Server TOP", () =>
{
    var query = new SelectQuery<Item>().SelectRootColumns().Top(10);
    Excludes(Sql(query, DbFlavor.PostgreSql), "TOP");
    Excludes(Sql(query, DbFlavor.MySql), "TOP");
    Contains(Sql(query, DbFlavor.SqlServer), "SELECT TOP 10");
    Excludes(Sql(query, DbFlavor.SqlServer), "FETCH NEXT");
});
foreach (DateTimeKind kind in Enum.GetValues<DateTimeKind>())
{
    Test($"insert leaves {kind} timestamp inference to the provider", () =>
    {
        var date = new DateTime(2025, 6, 15, 13, 14, 15, kind);
        var value = new SqlLinqer.Components.Insert.InsertRowValue("happened_at", date, null);
        Expect(value.DbType == null && (DateTime)value.Value == date && ((DateTime)value.Value).Kind == kind,
            "Insert forced a timestamp type or changed its value");
    });
}
Test("insert honors an explicitly requested timestamp type", () =>
{
    var value = new SqlLinqer.Components.Insert.InsertRowValue("happened_at", DateTime.UtcNow, DbType.DateTime2);
    Expect(value.DbType == DbType.DateTime2, "Explicit timestamp type was discarded");
});
Test("paging stash can be reused", () =>
{
    var paging = new PagingControls();
    paging.SetPage(2, 10);
    Expect(paging.Stash(), "First stash failed");
    Expect(!paging.Stash(), "Nested stash overwrote saved state");
    paging.Reset(); paging.Unstash();
    Expect(paging.Limit == 10 && paging.Offset == 10, "Paging was not restored");
    paging.SetPage(3, 5); paging.Stash(); paging.Reset(); paging.Unstash();
    Expect(paging.Limit == 5 && paging.Offset == 10, "Second stash reused old state");
});
foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
{
    foreach (bool count in new[] { false, true })
    {
        Test($"failed {(count ? "count" : "select")} rendering restores query state for {flavor}", () =>
        {
            var query = new SelectQuery<Item>().SelectRootColumns().Page(2, 10);
            Throws<NotSupportedException>(() =>
            {
                if (count) query.RenderForCount(new ParameterCollection(), (DbFlavor)999);
                else query.Render((DbFlavor)999);
            });
            query.Page(3, 5);
            string first = query.Render(flavor).Text;
            Expect(query.Render(flavor).Text == first, "Stale render state overwrote the repaired query");
            Contains(first, flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY" : "LIMIT 5 OFFSET 10");
        });
    }
}
foreach (int rows in new[] { 0, 2, 10 })
{
    Test($"page total is independent of returned row count {rows}", () =>
    {
        var database = new FakeDatabase { Rows = rows };
        var result = new SelectQuery<Item>().SelectRootColumns().Page(3, 10)
            .ExecuteWithTotalCount(new FakeConnector(database));
        Expect(result.State == ResponseState.Valid, result.Error?.ToString());
        Expect(result.TotalResults == 22, "Incorrect total");
        Expect(result.Result.Count == rows, "Incorrect returned rows");
    });
}
Test("unpaged select does not execute a redundant count", () =>
{
    var database = new FakeDatabase();
    var result = new SelectQuery<Item>().SelectRootColumns().ExecuteWithTotalCount(new FakeConnector(database));
    Expect(result.TotalResults == 2 && database.Executed.Count == 1, "Unexpected count query");
});

foreach (string operation in new[] { "select", "delete", "aggregate", "exists", "procedure-json", "procedure-table" })
{
    Test($"{operation} overload forwards caller transaction", () =>
    {
        var database = new FakeDatabase();
        var connector = new FakeConnector(database);
        SqlLinqer.Default.Connector = connector;
        using var connection = new FakeConnection(database);
        connection.Open();
        using var transaction = (FakeTransaction)connection.BeginTransaction();
        ISqlResponse result = operation switch
        {
            "select" => new SelectQuery<Item>().SelectRootColumns().Execute(transaction),
            "delete" => new DeleteQuery<Item>().Where(x => x.Id, 7, SqlOp.EQ).Execute(transaction),
            "aggregate" => Item.BeginSelectBuilder().BuildQuery(x => new { Id = x.Select(y => y.Id) }).Execute(transaction),
            "exists" => new SelectPrimaryKeyExistsQuery<Item, int>(7).Execute(transaction),
            "procedure-json" => new StoredProcedureQuery<Item>("sample").ExecuteJson(transaction),
            _ => new StoredProcedureQuery<Item>("sample").ExecuteDataTable(transaction),
        };
        Expect(result.State == ResponseState.Valid, result.Error?.ToString());
        Expect(connector.Created == 0, "Created another connection instead of using the caller's");
        Expect(database.Executed.Single().UsedTransaction == transaction, "Transaction was not assigned to command");
        Expect(transaction.Commits == 0 && transaction.Rollbacks == 0 && !transaction.IsDisposed, "Connector completed caller transaction");
        Expect(!connection.IsDisposed && connection.State == ConnectionState.Open, "Connector closed caller connection");
        Expect(connection.Commands.All(x => x.IsDisposed), "Command was not disposed");
    });
}

foreach (string operation in new[] { "insert", "update", "delete" })
    foreach (bool fail in new[] { false, true })
        Test($"DuckDB {operation} outputs complete an owned transaction, failure={fail}", () =>
        {
            var database = new FakeDatabase { FailAt = fail ? "execute" : null };
            var connector = new FakeConnector(database, DbFlavor.DuckDb);
            ISqlResponse result = operation switch
            {
                "insert" => new InsertQuery<Item>().InsertAuto(new Item()).Output(x => x.Id).ExecuteWithOutput(connector),
                "update" => new UpdateQuery<Item>().Update(x => x.Name, "output").Output(x => x.Id).ExecuteWithOutput(connector),
                _ => new DeleteQuery<Item>().Where(x => x.Id, 7, SqlOp.EQ).Output(x => x.Id).ExecuteWithOutput(connector),
            };
            Expect(result.State == (fail ? ResponseState.Error : ResponseState.Valid), result.Error?.ToString());
            var connection = database.Connections.Single();
            var transaction = connection.Transactions.Single();
            Expect(transaction.IsolationLevel == IsolationLevel.Snapshot, "Output write did not use DuckDB isolation");
            Expect(database.Executed.All(x => x.UsedTransaction == transaction), "Output command was outside its owned transaction");
            Expect(transaction.Commits == (fail ? 0 : 1) && transaction.Rollbacks == (fail ? 1 : 0), "Output write completed the transaction incorrectly");
            Expect(transaction.IsDisposed && connection.IsDisposed, "Output write leaked owned resources");
        });

var executionMethods = new Dictionary<string, Func<BaseConnector, RenderedQuery, DbTransaction, ISqlResponse>>
{
    ["nonquery"] = (c, q, t) => c.ExecuteNonQuery(q, t),
    ["scalar"] = (c, q, t) => c.ExecuteScalar<long>(q, t),
    ["json"] = (c, q, t) => c.ExecuteReaderJson<Item>(q, t),
    ["table"] = (c, q, t) => c.ExecuteReaderDataTable(q, t),
};
foreach (var method in executionMethods)
{
    foreach (string failure in new[] { "open", "command", "parameter", "execute" })
    {
        Test($"{method.Key} disposes owned resources after {failure} failure", () =>
        {
            var database = new FakeDatabase { FailAt = failure };
            var query = new RenderedQuery("SELECT @P1"); query.Parameters.AddParameter(1);
            var result = method.Value(new FakeConnector(database), query, null);
            Expect(result.State == ResponseState.Error, "Failure was not reported");
            Expect(database.Connections.Single().IsDisposed, "Connection leaked");
            Expect(database.Connections.Single().Commands.All(x => x.IsDisposed), "Command leaked");
        });
    }
    Test($"{method.Key} leaves caller transaction and connection owned by caller on failure", () =>
    {
        var database = new FakeDatabase { FailAt = "execute" };
        var connector = new FakeConnector(database);
        using var connection = new FakeConnection(database); connection.Open();
        using var transaction = (FakeTransaction)connection.BeginTransaction();
        var result = method.Value(connector, new RenderedQuery("SELECT 1"), transaction);
        Expect(result.State == ResponseState.Error, "Failure was not reported");
        Expect(!transaction.IsDisposed && transaction.Rollbacks == 0 && transaction.Commits == 0, "Changed caller transaction");
        Expect(!connection.IsDisposed && connection.State == ConnectionState.Open, "Closed caller connection");
    });
    Test($"{method.Key} rejects a completed transaction without opening another connection", () =>
    {
        var database = new FakeDatabase();
        var connector = new FakeConnector(database);
        using var connection = new FakeConnection(database);
        using var transaction = new FakeTransaction(connection, IsolationLevel.ReadCommitted) { Completed = true };
        Expect(method.Value(connector, new RenderedQuery("SELECT 1"), transaction).State == ResponseState.Error, "Completed transaction accepted");
        Expect(connector.Created == 0, "Opened an unrelated connection");
    });
}
Test("owned transaction commits and disposes on success", () =>
{
    var database = new FakeDatabase();
    var result = new FakeConnector(database).ExecuteNonQuery(new RenderedQuery("DELETE FROM sample"));
    Expect(result.State == ResponseState.Valid && result.Result == 1, "Write failed");
    var transaction = database.Connections.Single().Transactions.Single();
    Expect(transaction.Commits == 1 && transaction.Rollbacks == 0 && transaction.IsDisposed, "Owned transaction not completed");
    Expect(database.Connections.Single().IsDisposed, "Owned connection leaked");
});
Test("rollback failure preserves original query error", () =>
{
    var database = new FakeDatabase { FailAt = "execute", FailRollback = true };
    var result = new FakeConnector(database).ExecuteNonQuery(new RenderedQuery("DELETE FROM sample"));
    Expect(result.Error.InnerException?.Message == "execute", "Rollback hid query error");
    Expect(database.Connections.Single().IsDisposed, "Connection leaked after rollback failure");
});

foreach (bool fail in new[] { false, true })
{
    Test($"TVP uses caller connection and transaction, failure={fail}", () =>
    {
        var database = new FakeDatabase();
        var connector = new FakeConnector(database, DbFlavor.SqlServer);
        using var connection = new FakeConnection(database); connection.Open();
        using var transaction = (FakeTransaction)connection.BeginTransaction();
        if (fail) database.FailAt = "execute";
        var result = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item()).ExecuteTvp(connector, transaction);
        Expect(result.State == (fail ? ResponseState.Error : ResponseState.Valid), "Unexpected TVP response");
        Expect(connector.Created == 0, "TVP created another connection");
        Expect(database.Executed.All(x => x.UsedTransaction == transaction), "TVP command ignored caller transaction");
        Expect(!transaction.IsDisposed && transaction.Commits == 0 && transaction.Rollbacks == 0, "TVP completed caller transaction");
        Expect(!connection.IsDisposed && connection.State == ConnectionState.Open, "TVP closed caller connection");
    });
}

foreach (bool fail in new[] { false, true })
{
    Test($"TVP completes its owned transaction, failure={fail}", () =>
    {
        var database = new FakeDatabase { FailAt = fail ? "execute" : null };
        var connector = new FakeConnector(database, DbFlavor.SqlServer);
        var result = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item()).ExecuteTvp(connector);
        Expect(result.State == (fail ? ResponseState.Error : ResponseState.Valid), "Unexpected TVP response");
        Expect(connector.Created == 1, "TVP split the operation across connections");
        var connection = database.Connections.Single();
        var transaction = connection.Transactions.Single();
        Expect(transaction.IsDisposed && connection.IsDisposed, "TVP leaked resources");
        Expect(transaction.Commits == (fail ? 0 : 1) && transaction.Rollbacks == (fail ? 1 : 0), "Incorrect TVP transaction result");
    });
}

Test("PostgreSQL inserts put RETURNING after VALUES", () =>
{
    string sql = Sql(new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item { Name = "sample" }), DbFlavor.PostgreSql);
    Contains(sql, "INSERT INTO \"items\"");
    Contains(sql, "RETURNING \"Id\""); Excludes(sql, "OUTPUT");
    Expect(sql.IndexOf("RETURNING") > sql.IndexOf("VALUES"), "RETURNING precedes VALUES");
});
Test("PostgreSQL updates use physical target and unqualified assignment", () =>
{
    var query = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.Name, "sample").Where(x => x.Id, 7, SqlOp.EQ);
    string sql = Sql(query, DbFlavor.PostgreSql);
    Contains(sql, "UPDATE \"items\" AS"); Contains(sql, "SET \"Name\" = @"); Contains(sql, "RETURNING");
    Excludes(sql, "OUTPUT"); Excludes(sql, " FROM ");
});
Test("PostgreSQL arithmetic and column assignments use valid targets", () =>
{
    Contains(Sql(new UpdateQuery<Item>().Update(x => x.Id, new BasicMathOperation(BasicMathOperation.MathOp.Add, 1)), DbFlavor.PostgreSql), "SET \"Id\" =");
    Contains(Sql(new UpdateQuery<Item>().Update(x => x.Name, x => x.OtherName), DbFlavor.PostgreSql), "SET \"Name\" =");
});
Test("PostgreSQL deletes include RETURNING", () =>
{
    string sql = Sql(new DeleteWithPrimaryKeyQuery<Item, int>().Where(x => x.Id, 7, SqlOp.EQ), DbFlavor.PostgreSql);
    Contains(sql, "DELETE FROM \"items\" AS"); Contains(sql, "RETURNING"); Excludes(sql, "OUTPUT");
});
foreach (DbFlavor flavor in new[] { DbFlavor.PostgreSql, DbFlavor.SqlServer })
{
    Test($"delete captures keys for {flavor}", () =>
    {
        var result = new DeleteWithPrimaryKeyQuery<Item, int>().Where(x => x.Id, 7, SqlOp.EQ)
            .Execute(new FakeConnector(new FakeDatabase(), flavor));
        Expect(result.State == ResponseState.Valid, result.Error?.ToString());
        Expect(result.Result == 1 && result.TouchedPrimaryKeys.Single() == 7, "Deleted keys were not captured");
    });
    Test($"delete output precedes FROM or follows WHERE for {flavor}", () =>
    {
        string sql = Sql(new DeleteQuery<Item>().Output(x => x.Id).Where(x => x.Id, 7, SqlOp.EQ), flavor);
        if (flavor == DbFlavor.SqlServer)
            Expect(sql.IndexOf("OUTPUT [Deleted].[Id]") < sql.IndexOf("FROM"), "Invalid OUTPUT position");
        else
            Expect(sql.IndexOf("RETURNING") > sql.IndexOf("WHERE"), "Invalid RETURNING position");
    });
}
Test("PostgreSQL joined update preserves joins and correlates target primary key", () =>
{
    string sql = Sql(new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.Name, x => x.Category.Name)
        .Where(x => x.Category.Name, "sample", SqlOp.EQ), DbFlavor.PostgreSql);
    Contains(sql, "LEFT JOIN \"categories\""); Contains(sql, "FROM \"items\"");
    Contains(sql, "WHERE \"target_"); Contains(sql, "AND ("); Contains(sql, "RETURNING \"target_");
});
Test("PostgreSQL joined delete preserves joins and correlates target primary key", () =>
{
    string sql = Sql(new DeleteWithPrimaryKeyQuery<Item, int>().Where(x => x.Category.Name, "sample", SqlOp.EQ), DbFlavor.PostgreSql);
    Contains(sql, "USING \"items\""); Contains(sql, "LEFT JOIN \"categories\""); Contains(sql, "WHERE \"target_");
});
Test("MySQL puts joined tables before SET", () =>
{
    string sql = Sql(new UpdateQuery<Item>().Update(x => x.Name, x => x.Category.Name), DbFlavor.MySql);
    Contains(sql, "UPDATE `items` AS"); Contains(sql, "LEFT JOIN `categories`");
    Expect(sql.IndexOf("LEFT JOIN") < sql.IndexOf(" SET "), "JOIN follows SET"); Excludes(sql, " FROM ");
});
Test("MySQL plain writes do not contain SQL Server output", () =>
{
    Excludes(Sql(new InsertQuery<Item>().InsertAuto(new Item { Name = "sample" }), DbFlavor.MySql), "OUTPUT");
    Contains(Sql(new DeleteQuery<Item>(), DbFlavor.MySql), "FROM `items`");
});
Test("MySQL returned-row requests fail before execution", () =>
{
    var database = new FakeDatabase(); var connector = new FakeConnector(database, DbFlavor.MySql);
    Throws<NotSupportedException>(() => new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item()).Execute(connector));
    Throws<NotSupportedException>(() => new UpdateQuery<Item>().Update(x => x.Name, "sample").Output(x => x.Id).ExecuteWithOutput(connector));
    Throws<NotSupportedException>(() => new DeleteWithPrimaryKeyQuery<Item, int>().Execute(connector));
    Expect(database.Connections.Count == 0, "An unsupported request touched the database");
});

foreach (string password in new[] { "abc=def", "abc;def=ghi", "both\"'quotes;=", "space at end " })
{
    Test($"connection password survives rebuilding: {password}", () =>
    {
        var builder = new DbConnectionStringBuilder { ["Host"] = "localhost", ["Password"] = password, ["Timeout"] = 30 };
        var connector = new ReplicatorConnector(new FakeConnection { ConnectionString = builder.ConnectionString }, DbFlavor.PostgreSql,
            new ConnectorOptions { ConnectionTimeout = TimeSpan.FromSeconds(7) });
        using var copy = connector.CreateConnection();
        var rebuilt = new DbConnectionStringBuilder { ConnectionString = copy.ConnectionString };
        Expect((string)rebuilt["Password"] == password, "Password was corrupted");
        Expect(rebuilt["Timeout"].ToString() == "7", "Timeout was not replaced");
    });
}
foreach (DbFlavor flavor in new[] { DbFlavor.MySql, DbFlavor.SqlServer })
{
    Test($"connection timeout aliases are replaced for {flavor}", () =>
    {
        var connector = new ReplicatorConnector(new FakeConnection { ConnectionString = "Server=localhost;ConnectionTimeout=30;CommandTimeout=99" }, flavor,
            new ConnectorOptions { ConnectionTimeout = TimeSpan.FromSeconds(7) });
        using var connection = connector.CreateConnection();
        var builder = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };
        Expect(!builder.ContainsKey("ConnectionTimeout"), "Left a duplicate timeout alias");
        Expect(builder["Connect Timeout"].ToString() == "7", "Incorrect timeout");
        Expect(builder["CommandTimeout"].ToString() == "99", "Changed command timeout");
    });
}
foreach (object value in new object[] { null, DBNull.Value, SqlNull.NULL })
{
    Test($"NULL diagnostic formatting accepts {value?.GetType().Name ?? "null"}", () =>
    {
        var query = new RenderedQuery(); query.Text = "SELECT " + query.Parameters.AddParameter(value);
        Expect(query.GetTextWithParameterValues() == "SELECT NULL", "Incorrect NULL text");
        Expect(query.ToString() == "SELECT NULL", "ToString threw or formatted incorrectly");
    });
}

AzureSqlRegressionTests.Run(Test);
JsonRegressionTests.Run(Test);
CompatibleQueryRegressionTests.Run(Test);
if (args.Contains("--benchmark-render")) CompatibleQueryRegressionTests.MeasureRendering();

SqlLinqer.Default.Connector = null;
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

[SqlTable("items"), SqlAutoInsert]
public class Item : SqlLinqerPrimaryKeyObject<Item, int>
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
    public string OtherName { get; set; }
    public bool Enabled { get; set; }
    public int CategoryId { get; set; }
    [SqlOneToOne(nameof(CategoryId))] public Category Category { get; set; }
}
[SqlTable("categories")]
public class Category
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
}
internal sealed class RowJsonConnector(FakeDatabase database, DbFlavor flavor) : BaseConnector(flavor, new ConnectorOptions())
{
    public override DbConnection CreateConnection() => new FakeConnection(database);
    public override bool JsonResultsRowByRow() => true;
}
