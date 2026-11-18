using System.Data;
using SqlLinqer;
using SqlLinqer.Exceptions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

// Awaitable response helpers and the connector's open-connection helper.
internal static class ExecutionHelperRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("awaited responses return results and rethrow recorded errors", () =>
        {
            async Task Run()
            {
                Check(await Task.FromResult(new SqlResponse<int>(5)).GetResultOrThrowAsync() == 5, "A successful result changed");
                var page = new SqlSelectResponse<Row>();
                page.SetResult(new List<Row> { new Row { Id = 1 } }, 9);
                Check((await Task.FromResult(page).GetResultOrThrowAsync()).Count == 1, "Select rows changed");
                Check((await Task.FromResult(page).EnsureSuccessAsync()).TotalResults == 9, "EnsureSuccessAsync lost the concrete response type");

                var database = new FakeDatabase();
                var connector = new FakeConnector(database);
                var keys = await new UpdateWithPrimaryKeyQuery<Row, int>().Update(x => x.Label, "a").Where(x => x.Id, 1, SqlOp.EQ)
                    .ExecuteAsync(connector).EnsureSuccessAsync();
                Check(keys.TouchedPrimaryKeys != null, "A primary-key response lost its keys");
                Check(await new UpdateQuery<Row>().Update(x => x.Label, "a").Where(x => x.Id, 1, SqlOp.EQ).ExecuteAsync(connector).GetResultOrThrowAsync() == 1,
                    "An awaited affected-row count changed");

                database.FailAt = "execute";
                try { await new UpdateQuery<Row>().Update(x => x.Label, "a").Where(x => x.Id, 1, SqlOp.EQ).ExecuteAsync(connector).GetResultOrThrowAsync(); }
                catch (SqlResponseException) { goto ensured; }
                throw new Exception("A failed response did not throw its recorded error");
            ensured:
                try { await new UpdateQuery<Row>().Update(x => x.Label, "a").Where(x => x.Id, 1, SqlOp.EQ).ExecuteAsync(connector).EnsureSuccessAsync(); }
                catch (SqlResponseException) { return; }
                throw new Exception("EnsureSuccessAsync did not throw the recorded error");
            }
            Run().GetAwaiter().GetResult();
        });
        foreach (bool asynchronous in new[] { false, true })
            test($"OpenConnection returns an open caller-owned connection and disposes a failed one, async={asynchronous}", () =>
            {
                var database = new FakeDatabase();
                var connector = new FakeConnector(database);
                using (var connection = asynchronous ? connector.OpenConnectionAsync().GetAwaiter().GetResult() : connector.OpenConnection())
                {
                    Check(connection.State == ConnectionState.Open && !((FakeConnection)connection).IsDisposed, "The connection was not returned open");
                    Check(connector.Created == 1 && database.Connections.Sum(c => c.Transactions.Count) == 0, "Opening created extra connections or a transaction");
                }
                database.FailAt = "open";
                try
                {
                    if (asynchronous) connector.OpenConnectionAsync().GetAwaiter().GetResult(); else connector.OpenConnection();
                    throw new Exception("A failed open did not throw");
                }
                catch (InvalidOperationException error) when (error.Message == "open") { }
                Check(database.Connections.Last().IsDisposed, "A connection that failed to open was not disposed");
            });
        test("OpenConnectionAsync honors cancellation and leaves an open factory connection alone", () =>
        {
            var database = new FakeDatabase();
            var connector = new FakeConnector(database);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { connector.OpenConnectionAsync(cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation was ignored"); }
            catch (OperationCanceledException) { }
            Check(connector.Created == 0, "A cancelled open created a connection");

            using var shared = new FakeConnection(new FakeDatabase { FailAt = "open" });
            shared.DatabaseState.FailAt = null;
            shared.Open();
            shared.DatabaseState.FailAt = "open";
            var factory = new SqlLinqer.Connections.ConnectionFactoryConnector(() => shared, DbFlavor.PostgreSql);
            Check(ReferenceEquals(factory.OpenConnection(), shared) && shared.State == ConnectionState.Open, "An already open connection was reopened");
        });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    [SqlTable("helper_rows")]
    private sealed class Row
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("label")] public string Label { get; set; }
    }
}
