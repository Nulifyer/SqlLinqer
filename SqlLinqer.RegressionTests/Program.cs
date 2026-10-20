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

int passed = 0, failed = 0;
void Test(string name, Action run)
{
    try { run(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
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
