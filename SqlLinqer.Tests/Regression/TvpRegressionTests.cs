using System.Data;
using SqlLinqer;
using SqlLinqer.Queries.Typed;
using Row = SqlLinqer.Tests.Regression.CompilationRegressionTests.Row;

namespace SqlLinqer.Tests.Regression;

internal static class TvpRegressionTests
{
    internal static void CheckTypeDdl(FakeDatabase database)
    {
        var ddl = database.Executed.Where(x => x.CommandText.StartsWith("CREATE TYPE") || x.CommandText.StartsWith("DROP TYPE")).ToArray();
        Check(ddl.Length >= 2, "TVP did not attempt both type creation and cleanup");
        Check(ddl.All(x => x.UsedTransaction == null), "TVP type DDL ran inside a transaction and can self-deadlock");
        Check(database.Connections.Where(x => x.Transactions.Count == 0).All(x => x.IsDisposed), "Type DDL connection leaked");
    }

    internal static void Run(Action<string, Action> test)
    {
        foreach (DbFlavor flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
        foreach (bool caller in new[] { false, true })
        foreach (bool output in new[] { false, true })
        foreach (string stage in new[] { "CREATE TYPE", "INSERT INTO", "DROP TYPE" })
        {
            test($"async {flavor} TVP failure preserves ownership and cleans type, stage={stage}, caller={caller}, output={output}", () => RunFailure().GetAwaiter().GetResult());

            async Task RunFailure()
            {
                var database = new FakeDatabase { FailSql = stage };
                var connector = new FakeConnector(database, flavor);
                using var callerConnection = caller ? new FakeConnection(database) : null;
                callerConnection?.Open();
                using var transaction = caller ? (FakeTransaction)callerConnection.BeginTransaction() : null;
                var query = new InsertQuery<Row>().InsertAuto(new Row { Name = "TVP" });
                ISqlResponse response = output ? await query.Output(x => x.Id).ExecuteTvpWithOutputAsync(connector, transaction)
                    : await query.ExecuteTvpAsync(connector, transaction);
                Check(response.State == ResponseState.Error && response.Error.ToString().Contains("SQL stage failure: " + stage), "Original failure was lost");
                CheckTypeDdl(database);
                var writeConnection = database.Connections.First();
                Check(database.Executed.Where(x => x.CommandText.StartsWith("INSERT INTO"))
                    .All(x => x.UsedTransaction == writeConnection.Transactions.Single()), "Insert lost its write transaction");
                if (caller)
                {
                    Check(transaction.Commits == 0 && transaction.Rollbacks == 0 && !transaction.IsDisposed
                        && !callerConnection.IsDisposed && callerConnection.State == ConnectionState.Open, "Caller resources were completed");
                }
                else
                {
                    Check(database.Connections.All(x => x.IsDisposed), "Owned connection leaked");
                    if (stage == "CREATE TYPE") Check(writeConnection.Transactions.Count == 0, "Write transaction began before type creation succeeded");
                    else
                    {
                        var owned = writeConnection.Transactions.Single();
                        Check(owned.Commits == 0 && owned.Rollbacks == 1 && owned.IsDisposed, "Failed owned write did not roll back");
                    }
                }
                if (stage == "DROP TYPE") Check(response.Error.Data["TvpTypeCleanupError"] is Exception, "Metadata cleanup failure was lost");
                Check(database.SyncCommands == 0, "Async TVP used synchronous commands");
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
