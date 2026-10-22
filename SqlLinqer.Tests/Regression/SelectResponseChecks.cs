using SqlLinqer.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class SelectResponseChecks
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (bool projection in new[] { false, true })
            foreach (bool asynchronous in new[] { false, true })
                foreach (int failingCommand in new[] { 1, 2 })
                    test($"select count failures clear rows and totals, projection={projection}, async={asynchronous}, command={failingCommand}", () =>
                    {
                        var database = new FakeDatabase { FailOnExecution = failingCommand };
                        var connector = new FakeConnector(database);
                        var query = Query(projection);
                        var response = asynchronous
                            ? ((IAsyncQueryExecutableTotalCount<Row>)query).ExecuteWithTotalCountAsync(connector).GetAwaiter().GetResult()
                            : query.ExecuteWithTotalCount(connector);
                        Check(response.State == ResponseState.Error && response.Result == null && response.TotalResults == 0
                            && response.Error.ToString().Contains("execution " + failingCommand), "Failure retained rows or lost its cause");
                        Check(database.Executed.Count == failingCommand && database.Connections.All(c => c.IsDisposed
                            && c.Commands.All(command => command.IsDisposed)), "Failed select or count leaked a command or connection");
                    });
        foreach (bool projection in new[] { false, true })
            test($"cancellation between select and count propagates its token, projection={projection}", () =>
            {
                var database = new FakeDatabase();
                using var cancellation = new CancellationTokenSource();
                database.BeforeAsyncCommand = _ =>
                {
                    if (database.AsyncCommands == 1) cancellation.Cancel();
                    return Task.CompletedTask;
                };
                try
                {
                    ((IAsyncQueryExecutableTotalCount<Row>)Query(projection)).ExecuteWithTotalCountAsync(new FakeConnector(database),
                        cancellationToken: cancellation.Token).GetAwaiter().GetResult();
                    throw new Exception("Expected count cancellation");
                }
                catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }
                Check(database.Executed.Count == 1 && database.Connections.Count == 2
                    && database.Connections.All(c => c.IsDisposed && c.Commands.All(command => command.IsDisposed)),
                    "Cancelled count executed or leaked owned resources");
            });
    }

    private static IQueryExecutableTotalCount<Row> Query(bool projection)
        => projection ? new SelectQueryInitializer<Row>().BuildQuery(x => new Row { Id = x.Select(y => y.Id) }).Top(1)
            : new SelectQuery<Row>().SelectRootColumns().Top(1);

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [SqlTable("select_response_rows")]
    private sealed class Row { public int Id { get; set; } }
}
