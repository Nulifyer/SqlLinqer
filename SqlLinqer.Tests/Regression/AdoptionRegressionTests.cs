using System.Runtime.CompilerServices;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Diagnostics;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static class AdoptionRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("response helpers retain concrete metadata and successful empty values", () =>
        {
            var page = new SqlSelectResponse<PlainRow>();
            page.SetResult([], 42);
            Check(ReferenceEquals(page.EnsureSuccess(), page) && page.EnsureSuccess().TotalResults == 42, "Page metadata or identity changed");
            var keys = new SqlPrimaryKeysResponse<int, int>();
            keys.SetResult(0, [7]);
            Check(ReferenceEquals(keys.EnsureSuccess(), keys) && keys.EnsureSuccess().TouchedPrimaryKeys.SequenceEqual([7]), "Key metadata changed");
            Check(new SqlResponse<int>(0).GetResultOrThrow() == 0 && !new SqlResponse<bool>(false).GetResultOrThrow(), "Successful zero or false failed");
            Check(new SqlResponse<object>((object)null).GetResultOrThrow() == null && page.GetResultOrThrow().Count == 0, "Successful null or empty result failed");
        });
        test("response helpers rethrow the original failure with its stack and cancellation token", () =>
        {
            Exception original;
            try { OriginalFailure(); throw new Exception("Fixture did not throw"); }
            catch (InvalidOperationException error) { original = error; }
            try { new SqlResponse<int>(original).GetResultOrThrow(); throw new Exception("Failure was ignored"); }
            catch (InvalidOperationException error)
            { Check(ReferenceEquals(error, original) && error.StackTrace.Contains(nameof(OriginalFailure)), "Original error or stack was lost"); }
            using var source = new CancellationTokenSource(); source.Cancel();
            var cancelled = new OperationCanceledException(source.Token);
            try { new SqlResponse<int>(cancelled).EnsureSuccess(); throw new Exception("Cancellation was ignored"); }
            catch (OperationCanceledException error) { Check(ReferenceEquals(error, cancelled) && error.CancellationToken == source.Token, "Cancellation changed"); }
            try { SqlResponseExtensions.EnsureSuccess<SqlResponse<int>>(null); throw new Exception("Null response accepted"); }
            catch (ArgumentNullException) { }
        });
        test("connector factories bind schema and all builders without creating connections", () =>
        {
            var database = new FakeDatabase();
            var connector = new ConnectionFactoryConnector(() => new FakeConnection(database), DbFlavor.PostgreSql,
                new ConnectorOptions { DefaultSchema = "tenant" });
            var queries = new SqlLinqer.Queries.Core.Abstract.BaseQuery[]
            {
                connector.BeginSelect<PlainRow>().SelectRootColumns(), connector.BeginSelectAuto<PlainRow>(),
                connector.BeginInsert<PlainRow>(), connector.BeginInsert<PlainRow, int>(),
                connector.BeginUpdate<PlainRow>(), connector.BeginDelete<PlainRow>(), connector.BeginTruncate<PlainRow>(),
                connector.BeginUpdate<PlainRow, int>(), connector.BeginDelete<PlainRow, int>()
            };
            Check(queries.All(q => ReferenceEquals(q.BoundConnector, connector)), "A query did not retain the connector");
            Check(queries[0].Render(connector).Text.Contains("\"tenant\"."), "Factory ignored default schema");
            var projection = connector.BeginSelectBuilder<PlainRow>().BuildQuery(q => new { Id = q.Select(x => x.Id) });
            Check(ReferenceEquals(projection.BoundConnector, connector) && database.Connections.Count == 0, "Projection binding or lazy construction failed");
        });
        foreach (string mode in new[] { "sync", "async", "stream" })
        {
            test($"bound plain-class queries retain execution and diagnostic lifetime, {mode}", () => Read(mode).GetAwaiter().GetResult());
            test($"observer failures do not change successful reads, {mode}", () => Read(mode, new FaultingObserver()).GetAwaiter().GetResult());
        }
        test("diagnostic tags survive capture and count without changing SQL", () =>
        {
            var connector = new ConnectionFactoryConnector(() => throw new Exception("Must not open"), DbFlavor.PostgreSql);
            var query = connector.BeginSelect<PlainRow>().SelectRootColumns().Page(2, 3);
            string original = query.Render(connector).Text;
            var captured = query.Tag("users.search").Capture();
            query.Tag("users.changed");
            Check(captured.Render(connector).QueryName == "users.search" && captured.RenderForCount(connector.DbFlavor).QueryName == "users.search", "Capture or count lost the name");
            Check(query.Render(connector).Text == original, "Tag changed SQL");
        });
        test("observers retain execution errors after resource cleanup", () =>
        {
            var database = new FakeDatabase { FailAt = "execute" };
            var observer = new RecordingObserver(database);
            var connector = new ConnectionFactoryConnector(() => new FakeConnection(database), DbFlavor.PostgreSql, new ConnectorOptions { Observer = observer });
            var response = connector.BeginSelect<PlainRow>().SelectRootColumns().Execute();
            Check(response.State == ResponseState.Error && ReferenceEquals(observer.Results.Single().Error, response.Error), "Observer replaced or lost the failure");
            Check(observer.Results[0].Outcome == QueryExecutionOutcome.Failed && observer.Results[0].CommandDuration.HasValue, "Failure timing missing");
        });
    }

    private static async Task Read(string mode, IQueryObserver overrideObserver = null)
    {
        var database = new FakeDatabase { JsonRows = ["{\"Id\":7,\"Name\":\"Ada\"}", "{\"Id\":8,\"Name\":\"Grace\"}"] };
        var observer = new RecordingObserver(database);
        var connector = new ConnectionFactoryConnector(() => new FakeConnection(database), DbFlavor.PostgreSql,
            new ConnectorOptions { Observer = overrideObserver ?? observer });
        var query = connector.BeginSelect<PlainRow>().SelectRootColumns().Tag("users.read");
        if (mode == "stream")
        {
            var stream = query.ExecuteAsyncStream();
            Check(observer.Results.Count == 0 && database.Connections.Count == 0, "Stream executed before enumeration");
            await foreach (var row in stream) { Check(row.Id == 7, "Wrong first row"); await Task.Delay(20); break; }
        }
        else
        {
            var rows = mode == "sync" ? query.Execute().GetResultOrThrow() : (await query.ExecuteAsync()).GetResultOrThrow();
            Check(rows.Select(x => x.Id).SequenceEqual([7, 8]), "Wrong buffered rows");
        }
        Check(database.Connections.Count == 1 && database.Connections[0].IsDisposed, "Connection lifetime changed");
        if (overrideObserver != null) return;
        var result = observer.Results.Single();
        Check(observer.Contexts.Single().Query.QueryName == "users.read", "Diagnostic name missing");
        Check(result.ReturnedRows == (mode == "stream" ? 1 : 2) && result.CommandDuration.HasValue && result.Duration >= result.CommandDuration.Value,
            "Row count or command timing is wrong");
        Check(result.Outcome == (mode == "stream" ? QueryExecutionOutcome.StoppedEarly : QueryExecutionOutcome.Completed), "Wrong outcome");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OriginalFailure() => throw new InvalidOperationException("original");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [SqlTable("adoption_rows")]
    private sealed class PlainRow
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
    }
    private sealed class RecordingObserver(FakeDatabase database) : IQueryObserver
    {
        internal readonly List<QueryExecutionResult> Results = [];
        internal readonly List<QueryExecutionContext> Contexts = [];
        public IQueryObservation Start(QueryExecutionContext context) { Contexts.Add(context); return new Completion(this, database); }
        private sealed class Completion(RecordingObserver owner, FakeDatabase database) : IQueryObservation
        {
            public void Complete(QueryExecutionResult result)
            { Check(database.Connections.All(c => c.IsDisposed), "Observation ended before connection cleanup"); owner.Results.Add(result); }
        }
    }
    private sealed class FaultingObserver : IQueryObserver, IQueryObservation
    {
        public IQueryObservation Start(QueryExecutionContext context) => this;
        public void Complete(QueryExecutionResult result) => throw new InvalidOperationException("observer failure");
    }
}
