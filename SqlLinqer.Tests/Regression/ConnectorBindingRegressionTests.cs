using System.Data.Common;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static class ConnectorBindingRegressionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Run(Action<string, Action> test)
    {
        var factories = new Dictionary<string, Func<BaseConnector, Func<DbTransaction, TimeSpan?, ISqlResponse>>>
        {
            ["select"] = c => Item.BeginSelect(c).SelectRootColumns().Execute,
            ["select-auto"] = c => Item.BeginSelectAuto(c).Execute,
            ["select-key"] = c => Item.BeginSelect(7, c).SelectRootColumns().Execute,
            ["select-key-auto"] = c => Item.BeginSelectAuto(7, c).Execute,
            ["select-count"] = c => Item.BeginSelect(c).SelectRootColumns().Top(1).ExecuteWithTotalCount,
            ["aggregate"] = c => Item.BeginSelectBuilder(c).BuildQuery(x => new { Id = x.Select(y => y.Id) }).Execute,
            ["aggregate-count"] = c => Item.BeginSelectBuilder(c).BuildQuery(x => new { Id = x.Select(y => y.Id) }).Top(1).ExecuteWithTotalCount,
            ["insert"] = c => PlainItem.BeginInsert(c).InsertAuto(new PlainItem()).Execute,
            ["insert-auto"] = c => new PlainItem().BeginInsertAuto(c).Execute,
            ["insert-output"] = c => PlainItem.BeginInsert(c).InsertAuto(new PlainItem()).Output(x => x.Id).ExecuteWithOutput,
            ["insert-key"] = c => Item.BeginInsert(c).InsertAuto(new Item()).Execute,
            ["insert-key-auto"] = c => new Item().BeginInsertAuto(c).Execute,
            ["insert-tvp"] = c => PlainItem.BeginInsert(c).InsertAuto(new PlainItem()).ExecuteTvp,
            ["insert-tvp-output"] = c => PlainItem.BeginInsert(c).InsertAuto(new PlainItem()).Output(x => x.Id).ExecuteTvpWithOutput,
            ["insert-key-tvp"] = c => Item.BeginInsert(c).InsertAuto(new Item()).ExecuteTvp,
            ["update"] = c => Item.BeginUpdate(c).Update(x => x.Name, "changed").Where(x => x.Id, 7, SqlOp.EQ).Execute,
            ["update-output"] = c => Item.BeginUpdate(c).Update(x => x.Name, "changed").Output(x => x.Id).ExecuteWithOutput,
            ["update-key"] = c => Item.BeginUpdate(7, c).Update(x => x.Name, "changed").Execute,
            ["update-key-auto"] = c => new AutoItem { Id = 7, Name = "changed" }.BeginUpdateAuto(c).Execute,
            ["update-key-nondefaults"] = c => new Item { Id = 7, Name = "changed" }.BeginUpdateNonDefaults(c).Execute,
            ["delete"] = c => PlainItem.BeginDelete(c).Where(x => x.Id, 7, SqlOp.EQ).Execute,
            ["delete-output"] = c => PlainItem.BeginDelete(c).Where(x => x.Id, 7, SqlOp.EQ).Output(x => x.Id).ExecuteWithOutput,
            ["delete-key"] = c => Item.BeginDelete(c).Where(x => x.Id, 7, SqlOp.EQ).Execute,
            ["delete-key-value"] = c => Item.BeginDelete(7, c).Execute,
            ["delete-key-auto"] = c => new Item { Id = 7 }.BeginDeleteAuto(c).Execute,
            ["exists"] = c => Item.BeginPrimaryKeyExists(7, c).Execute,
            ["truncate"] = c => Item.BeginTruncate(c).Execute,
        };

        foreach (var factory in factories)
        {
            var flavor = factory.Key.Contains("tvp") ? DbFlavor.SqlServer : DbFlavor.PostgreSql;
            test($"{factory.Key} retains its connector and forwards caller transactions", () =>
            {
                var previous = Default.Connector;
                try
                {
                    var database = new FakeDatabase();
                    var connector = new FakeConnector(database, flavor);
                    var otherDatabase = new FakeDatabase { FailAt = "open" };
                    Default.Connector = new FakeConnector(otherDatabase, flavor);
                    var execute = factory.Value(connector);
                    Check(database.Connections.Count == 0, "Query construction opened a connection");

                    var response = execute(null, TimeSpan.FromSeconds(71));
                    Check(response.State == ResponseState.Valid, response.Error?.ToString());
                    Check(database.Executed.Count > 0 && otherDatabase.Connections.Count == 0,
                        "Execution used the global connector instead of the retained connector");
                    Check(database.Executed.All(command => command.CommandTimeout == 71), "Execution lost the per-call timeout");

                    database.Executed.Clear();
                    using var connection = new FakeConnection(database);
                    connection.Open();
                    using var transaction = (FakeTransaction)connection.BeginTransaction();
                    int connections = database.Connections.Count;
                    response = execute(transaction, TimeSpan.FromSeconds(71));
                    Check(response.State == ResponseState.Valid, response.Error?.ToString());
                    bool tvp = factory.Key.Contains("tvp");
                    Check(database.Connections.Count == connections + (tvp ? 1 : 0) && database.Executed.Count > 0
                        && database.Executed.Where(x => !tvp || x.CommandText.StartsWith("INSERT INTO"))
                            .All(x => x.UsedTransaction == transaction), "Caller transaction was not forwarded");
                    Check(database.Executed.All(command => command.CommandTimeout == 71), "Caller transaction execution lost the per-call timeout");
                    if (tvp) Check(!database.Executed.Any(c => c.CommandText.Contains("DROP TYPE")), "Default TVP execution dropped the shared type");
                    Check(transaction.Commits == 0 && transaction.Rollbacks == 0 && !transaction.IsDisposed
                        && !connection.IsDisposed, "Execution took ownership of caller resources");
                }
                finally { Default.Connector = previous; }
            });

            test($"{factory.Key} without a binding uses the current default connector", () =>
            {
                var previous = Default.Connector;
                try
                {
                    var original = new FakeDatabase();
                    Default.Connector = new FakeConnector(original, flavor);
                    var execute = factory.Value(null);
                    var current = new FakeDatabase();
                    Default.Connector = new FakeConnector(current, flavor);
                    var response = execute(null, null);
                    Check(response.State == ResponseState.Valid, response.Error?.ToString());
                    Check(original.Connections.Count == 0 && current.Executed.Count > 0,
                        "Query without an explicit connector captured an old global default");

                    Default.Connector = null;
                    try { execute(null, null); }
                    catch (ArgumentNullException) { return; }
                    throw new InvalidOperationException("Missing connector was not rejected");
                }
                finally { Default.Connector = previous; }
            });
        }

        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
        {
            test($"explicit {flavor} connector renders and executes without replacing the binding", () =>
            {
                var source = new FakeDatabase();
                var sourceConnector = new RowJsonConnector(source, DbFlavor.PostgreSql);
                sourceConnector.Options.DefaultSchema = "source_schema";
                var target = new FakeDatabase();
                var targetConnector = new RowJsonConnector(target, flavor);
                targetConnector.Options.DefaultSchema = "target_schema";
                var query = Item.BeginSelect(sourceConnector).SelectRootColumns()
                    .Where(x => x.Name, "bound", SqlOp.EQ).Top(1);
                string originalSql = query.Render(sourceConnector).Text;
                string targetSql = query.Render(targetConnector).Text;

                var response = query.Execute(targetConnector);
                Check(response.State == ResponseState.Valid, response.Error?.ToString());
                Check(source.Connections.Count == 0 && target.Executed.Single().CommandText == targetSql,
                    "Explicit connector did not control rendering and execution");
                Check(targetSql.Contains("source_schema") && !targetSql.Contains("target_schema"),
                    "Execution unexpectedly changed the model schema");
                Check((string)target.Executed[0].Parameters[0].Value == "bound", "Override lost the query parameters");

                response = query.Execute();
                Check(response.State == ResponseState.Valid, response.Error?.ToString());
                Check(source.Executed.Single().CommandText == originalSql && target.Executed.Count == 1,
                    "Explicit execution replaced the retained connector or changed the query");

                var aggregate = Item.BeginSelectBuilder(sourceConnector)
                    .BuildQuery(x => new { Id = x.Select(y => y.Id) }).Top(1);
                var counted = aggregate.ExecuteWithTotalCount(targetConnector);
                Check(counted.State == ResponseState.Valid && counted.TotalResults == target.Total,
                    counted.Error?.ToString() ?? "Incorrect total");
                Check(source.Executed.Count == 1 && target.Executed.Count == 3,
                    "Aggregate select and count used different connectors");
                counted = aggregate.ExecuteWithTotalCount();
                Check(counted.State == ResponseState.Valid && source.Executed.Count == 3,
                    counted.Error?.ToString() ?? "Aggregate lost the original connector");
            });
        }
    }

    [SqlTable("plain_items"), SqlAutoInsert, SqlAutoUpdate]
    private sealed class PlainItem : SqlLinqerObject<PlainItem>
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
    }

    [SqlTable("auto_items"), SqlAutoUpdate]
    private sealed class AutoItem : SqlLinqerPrimaryKeyObject<AutoItem, int>
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
    }
}
