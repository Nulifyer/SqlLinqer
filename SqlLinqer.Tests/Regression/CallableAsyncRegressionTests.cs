using System.Data;
using SqlLinqer;
using SqlLinqer.Connections;

namespace SqlLinqer.Tests.Regression;

internal static partial class CallableRegressionTests
{
    internal static void RunAsync(Action<string, Action> test)
    {
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            test($"{flavor} async callable maps multiple sets and reads outputs after close", () => RunCallable(flavor).GetAwaiter().GetResult());
    }

    private static async Task RunCallable(DbFlavor flavor)
    {
        var database = Database(Users(), Totals());
        var connector = new FakeConnector(database, flavor);
        using var cancellation = new CancellationTokenSource();
        var result = AsyncRegressionTests.Result(await SearchUsers.Call(new SearchUsers.Args { ManagerId = 12, Counter = 5 }).ExecuteAsync(connector, cancellationToken: cancellation.Token));
        Check(result.Users.Single().Name == "雪" && result.Counts.Single() == 22 && result.Total == 22
            && result.Counter == 6 && result.Message == "done" && result.ReturnCode == 17, "Async callable results differ");
        Check(database.LastOpenToken == cancellation.Token && database.LastCommandToken == cancellation.Token
            && database.LastReadToken == cancellation.Token && database.LastNextToken == cancellation.Token, "Callable token propagation differs");
        Check(database.AsyncCommands == 1 && database.AsyncNextResults == 2 && database.AsyncReads > 0
            && database.SyncCommands == 0 && database.DisposedReaders == 1, "Callable used synchronous command contracts or leaked its reader");
        Check(database.Connections.Single().IsDisposed && database.Connections[0].Transactions.Single().Commits == 1,
            "Async callable ownership differs");
    }
}
