using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Data;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    private static DataTable Numbers(params int[] numbers)
    {
        var table = new DataTable(); table.Columns.Add("Value", typeof(int));
        foreach (int number in numbers) table.Rows.Add(number);
        return table;
    }

    private static FakeDatabase MultiDatabase(params DataTable[] tables) => new()
    {
        ReaderFactory = command => new ClosingReader(new DataTableReader(tables), () => SetReturnCode(command, 19), 3)
    };

    private static void RunMulti(Action<string, Action> test)
    {
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            foreach (bool asynchronous in new[] { false, true })
            {
                SqlMultiCallableResponse<(List<UserRow> Users, int[] Numbers)> Run(FakeDatabase db)
                {
                    var connector = new FakeConnector(db, flavor);
                    return asynchronous ? new MultiUsers().ExecuteAsync(connector).GetAwaiter().GetResult()
                        : new MultiUsers().Execute(connector);
                }
                test($"multi callable maps heterogeneous tables and metadata atomically, {flavor}, async={asynchronous}", () =>
                {
                    var db = MultiDatabase(new DataTable(), Users(), new DataTable(), Numbers(2, 2, 3), new DataTable());
                    var response = Run(db);
                    var (users, numbers) = response.GetResultOrThrow();
                    Check(users.Single().Name == "雪" && numbers.SequenceEqual(new[] { 2, 2, 3 })
                        && response.ReturnCode == 19 && response.RowsAffected == 3, "Lost rows, duplicates, order, or close-time metadata");
                    Check(db.Connections.Single().IsDisposed && db.Connections.Single().Transactions.Single().Commits == 1,
                        "Successful multi-call did not commit and dispose owned resources");
                    if (asynchronous) Check(db.SyncCommands == 0 && db.AsyncCommands == 1 && db.AsyncNextResults == 5
                        && db.DisposedReaders == 1, "Async call used sync execution or leaked its reader");
                });
                foreach (bool firstEmpty in new[] { false, true }) foreach (bool lastEmpty in new[] { false, true })
                    test($"multi callable preserves empty positions {firstEmpty}/{lastEmpty}, {flavor}, async={asynchronous}", () =>
                    {
                        var response = Run(MultiDatabase(Users(empty: firstEmpty), lastEmpty ? Numbers() : Numbers(42)));
                        Check(response.State == ResponseState.Valid && response.Result.Users.Count == (firstEmpty ? 0 : 1)
                            && response.Result.Numbers.Length == (lastEmpty ? 0 : 1), response.Error?.ToString());
                    });
                foreach (string failure in new[] { "zero", "missing", "extra", "schema", "conversion", "empty-schema", "reordered" })
                    test($"multi callable clears earlier tables on {failure}, {flavor}, async={asynchronous}", () =>
                    {
                        var second = failure == "empty-schema" ? Users(empty: true) : failure == "schema" ? Users() : Numbers(42);
                        if (failure == "conversion") { second = new DataTable(); second.Columns.Add("Value", typeof(string)); second.Rows.Add("not-an-int"); }
                        var tables = failure == "zero" ? new[] { new DataTable() } : failure == "missing" ? new[] { Users() } : failure == "extra" ? new[] { Users(), second, Numbers() }
                            : failure == "reordered" ? new[] { Numbers(), Users() } : new[] { Users(), second };
                        var db = MultiDatabase(tables);
                        var response = Run(db);
                        Check(response.State == ResponseState.Error && response.Error != null && response.Result == default
                            && response.ReturnCode == null && response.RowsAffected == null, "Failure retained partial output");
                        Check(db.Connections.Single().IsDisposed && db.Connections.Single().Transactions.Single().Rollbacks == 1,
                            "Failure did not roll back and dispose owned resources");
                    });
                test($"multi callable routes connectors and retains borrowed transactions, {flavor}, async={asynchronous}", () =>
                {
                    var previous = Default.Connector;
                    var db = MultiDatabase(Users(), Numbers(42)); var connector = new FakeConnector(db, flavor);
                    var unused = new FakeConnector(new FakeDatabase(), flavor);
                    try
                    {
                        Default.Connector = connector;
                        foreach (string route in new[] { "default", "retained", "explicit" })
                        {
                            var procedure = new MultiUsers(route == "retained" ? connector : route == "explicit" ? unused : null);
                            using var connection = new FakeConnection(db); connection.Open();
                            using var tx = (FakeTransaction)connection.BeginTransaction();
                            var response = route == "explicit"
                                ? asynchronous ? procedure.ExecuteAsync(connector, tx, commandTimeout: TimeSpan.FromSeconds(7)).GetAwaiter().GetResult()
                                    : procedure.Execute(connector, tx, commandTimeout: TimeSpan.FromSeconds(7))
                                : asynchronous ? procedure.ExecuteAsync(tx, commandTimeout: TimeSpan.FromSeconds(7)).GetAwaiter().GetResult()
                                    : procedure.Execute(tx, commandTimeout: TimeSpan.FromSeconds(7));
                            Check(response.State == ResponseState.Valid && tx.Commits == 0 && tx.Rollbacks == 0 && !tx.IsDisposed && !connection.IsDisposed
                                && db.Executed.Last().CommandTimeout == 7 && db.Executed.Last().UsedTransaction == tx, response.Error?.ToString());
                        }
                        Check(connector.Created == 0 && unused.Created == 0, "Borrowed transaction opened a connection");
                    }
                    finally { Default.Connector = previous; }
                });
                foreach (string stage in new[] { "open", "execute", "commit", "dispose-command", "dispose-connection", "dispose-transaction" })
                    test($"multi callable clears output on connector failure {stage}, {flavor}, async={asynchronous}", () =>
                    {
                        var db = MultiDatabase(Users(), Numbers(42)); db.FailAt = stage;
                        var response = Run(db);
                        Check(response.State == ResponseState.Error && response.Result == default && response.ReturnCode == null
                            && response.RowsAffected == null, "Connector failure retained output");
                    });
            }

        test("multi callable supports collection interfaces sets queues and immutable custom factories", () =>
        {
            SqlResultCollections.Register<int, ImmutableArray<int>>(rows => rows.ToImmutableArray());
            var connector = new FakeConnector(MultiDatabase(Users(), Numbers(1, 1, 2), Numbers(3, 4), Numbers(9, 10), Numbers(4), Numbers(5), Numbers(6), Numbers(7, 8), Numbers(8)), DbFlavor.SqlServer);
            var response = new ManyCollections().Execute(connector);
            var r = response.GetResultOrThrow();
            Check(r.Users.Single().Id == 7 && r.Set.SetEquals(new[] { 1, 2 }) && r.Queue.SequenceEqual(new[] { 3, 4 }) && r.Immutable.SequenceEqual(new[] { 9, 10 })
                && r.ReadOnly.Single() == 4 && r.Linked.Single() == 5 && r.Collection.Single() == 6
                && r.Stack.SequenceEqual(new[] { 8, 7 }) && r.Last.Single() == 8, "Collection materialization or tuple Rest was incorrect");
        });
        test("multi collections support all built-in interface declarations", () =>
        {
            Verify<List<int>>(); Verify<int[]>(); Verify<IList<int>>(); Verify<ICollection<int>>();
            Verify<IEnumerable<int>>(); Verify<IReadOnlyList<int>>(); Verify<IReadOnlyCollection<int>>();
            Verify<Collection<int>>(); Verify<ReadOnlyCollection<int>>(); Verify<ISet<int>>();
            void Verify<T>() where T : IEnumerable<int>
            {
                var connector = new FakeConnector(MultiDatabase(Numbers(1), Numbers(2, 3)), DbFlavor.SqlServer);
                var r = new CollectionPair<T>().Execute(connector).GetResultOrThrow();
                Check(r.Second.SequenceEqual(new[] { 2, 3 }), $"Incorrect materializer for {typeof(T)}");
            }
        });
        foreach (bool asynchronous in new[] { false, true })
            test($"multi callable shares immutable row mappings and nullable scalars, async={asynchronous}", () =>
            {
                var numbers = Numbers(); numbers.Rows.Add(DBNull.Value);
                var connector = new FakeConnector(MultiDatabase(Users(), numbers), DbFlavor.SqlServer);
                var response = asynchronous ? new ImmutableTables().ExecuteAsync(connector).GetAwaiter().GetResult()
                    : new ImmutableTables().Execute(connector);
                var r = response.GetResultOrThrow();
                Check(r.Users.Single().Name == "雪" && r.Numbers.Single() == null, "Immutable or nullable row mapping differs");
            });
        test("multi callable preserves empty middle tables", () =>
        {
            var db = MultiDatabase(Numbers(1), Numbers(), Numbers(3));
            var r = new ThreeTables().Execute(new FakeConnector(db, DbFlavor.SqlServer)).GetResultOrThrow();
            Check(r.First.Single() == 1 && r.Middle.Count == 0 && r.Last.Single() == 3, "Empty middle table shifted results");
        });
        foreach (bool asynchronous in new[] { false, true })
        {
            test($"multi custom collection failures clear all results, async={asynchronous}", () =>
            {
                foreach (bool nullResult in new[] { false, true })
                {
                    SqlResultCollections.Register<int, CustomNumbers>(rows => nullResult ? null : throw new InvalidOperationException("factory failed"));
                    var db = MultiDatabase(Users(), Numbers(42)); var connector = new FakeConnector(db, DbFlavor.SqlServer);
                    var response = asynchronous ? new CustomTables().ExecuteAsync(connector).GetAwaiter().GetResult() : new CustomTables().Execute(connector);
                    Check(response.State == ResponseState.Error && response.Result == default && response.ReturnCode == null
                        && db.Connections.Single().Transactions.Single().Rollbacks == 1, "Factory failure published partial output");
                }
            });
            test($"multi callable snapshots inputs and optional arguments, async={asynchronous}", () =>
            {
                var db = MultiDatabase(Users(), Numbers(42)); var connector = new FakeConnector(db, DbFlavor.SqlServer);
                var input = new MultiInput { Value = new SqlArgument<int?>(null) };
                var procedure = new MultiInputCall(connector);
                var response = asynchronous ? procedure.ExecuteAsync(input).GetAwaiter().GetResult() : procedure.Execute(input);
                Check(response.State == ResponseState.Valid && Parameter(db.Executed.Last(), "Value").Value == DBNull.Value, response.Error?.ToString());
                input.Value = default;
                Check(procedure.Render(input).Parameters.Count == 1, "Omitted input was bound or a global total was introduced");
                Throws<ArgumentNullException>(() => procedure.Execute(null));
            });
        }
        test("multi async cancellation before start and during later tables closes readers and rolls back", () =>
        {
            foreach (int cancelAfterReads in new[] { -1, 0, 2 })
            {
                using var cts = new CancellationTokenSource();
                var db = MultiDatabase(Users(), Numbers(42)); var connector = new FakeConnector(db, DbFlavor.SqlServer);
                if (cancelAfterReads < 0) cts.Cancel();
                else db.BeforeAsyncRead = _ => { if (db.AsyncReads == cancelAfterReads) cts.Cancel(); return Task.CompletedTask; };
                try { new MultiUsers().ExecuteAsync(connector, cancellationToken: cts.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
                catch (OperationCanceledException error) when (error.CancellationToken == cts.Token) { }
                if (cancelAfterReads < 0) Check(connector.Created == 0, "Pre-cancelled call opened a connection");
                else Check(db.DisposedReaders == 1 && db.Connections.Single().IsDisposed
                    && db.Connections.Single().Transactions.Single().Rollbacks == 1 && db.LastReadToken == cts.Token
                    && db.LastCommandToken == cts.Token, "Cancellation lost token or leaked resources");
            }
        });
        test("multi async execution snapshots TVPs before opening a connection", () =>
        {
            using var ids = new DataTable(); ids.Columns.Add("Id", typeof(int)); ids.Rows.Add(7);
            var db = MultiDatabase(Users(), Numbers(42));
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            db.BeforeAsyncCommand = _ => gate.Task;
            var input = new TvpInput { Ids = ids };
            var task = new MultiTvp().ExecuteAsync(input, new FakeConnector(db, DbFlavor.SqlServer));
            try { ids.Rows[0]["Id"] = 99; }
            finally { gate.SetResult(); }
            Check(task.GetAwaiter().GetResult().State == ResponseState.Valid, "TVP execution failed");
            using var frozen = (DataTable)Parameter(db.Executed.Single(), "Ids").Value;
            Check(!ReferenceEquals(ids, frozen) && (int)frozen.Rows[0]["Id"] == 7, "Multi async call observed mutated TVP");
        });
        test("multi custom factories are captured per call and later registration affects later calls", () =>
        {
            SqlResultCollections.Register<int, SnapshotNumbers>(rows => new SnapshotNumbers(rows));
            var db = MultiDatabase(Users(), Numbers(42));
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            db.BeforeAsyncCommand = _ => gate.Task;
            var connector = new FakeConnector(db, DbFlavor.SqlServer);
            var task = new SnapshotTables().ExecuteAsync(connector);
            try { SqlResultCollections.Register<int, SnapshotNumbers>(rows => new SnapshotNumbers(rows.Select(x => x + 1))); }
            finally { gate.SetResult(); }
            Check(task.GetAwaiter().GetResult().GetResultOrThrow().Numbers.Single() == 42, "Registration changed an in-flight factory");
            Check(new SnapshotTables().Execute(connector).GetResultOrThrow().Numbers.Single() == 43, "Later call did not use new factory");
            Throws<ArgumentNullException>(() => SqlResultCollections.Register<int, SnapshotNumbers>(null));
            Throws<ArgumentException>(() => SqlResultCollections.Register<object, IEnumerable<string>>(_ => Array.Empty<string>()));
        });
        foreach (bool asynchronous in new[] { false, true })
            test($"multi mapping failures preserve borrowed transaction ownership, async={asynchronous}", () =>
            {
                var db = MultiDatabase(Users(), Users()); var connector = new FakeConnector(db, DbFlavor.SqlServer);
                using var connection = new FakeConnection(db); connection.Open();
                using var tx = (FakeTransaction)connection.BeginTransaction();
                var response = asynchronous ? new MultiUsers().ExecuteAsync(connector, tx).GetAwaiter().GetResult() : new MultiUsers().Execute(connector, tx);
                Check(response.State == ResponseState.Error && response.Result == default && !connection.IsDisposed && !tx.IsDisposed
                    && tx.Rollbacks == 0 && tx.Commits == 0 && connector.Created == 0, "Failure changed borrowed ownership");
            });
        test("multi callable validates contracts before opening connections", () =>
        {
            var connector = new FakeConnector(new FakeDatabase(), DbFlavor.SqlServer);
            Throws<InvalidOperationException>(() => new InvalidMulti<List<int>>().Render(connector));
            Throws<InvalidOperationException>(() => new InvalidMulti<(int, int)>().Render(connector));
            Throws<InvalidOperationException>(() => new InvalidMulti<(List<int>, UnsupportedNumbers)>().Render(connector));
            Throws<InvalidOperationException>(() => new MissingMultiMapping());
            foreach (var flavor in new[] { DbFlavor.Sqlite, DbFlavor.DuckDb, DbFlavor.MySql, DbFlavor.PostgreSql })
                Throws<NotSupportedException>(() => new MultiUsers().Execute(new FakeConnector(new FakeDatabase(), flavor)));
            Check(connector.Created == 0, "Invalid contract opened a connection");
        });
        test("multi response state transitions clear metadata through base response references", () =>
        {
            var response = new SqlMultiCallableResponse<(List<int>, int[])>();
            Check(response.State == ResponseState.Error && response.Result == default, "Unset response was valid");
            response.SetResult((new List<int> { 1 }, new[] { 2 }), 19, 3);
            SqlResponse<(List<int>, int[])> parent = response;
            parent.SetError(new Exception("failure"));
            Check(response.Result == default && response.ReturnCode == null && response.RowsAffected == null, "Error retained metadata");
            response.SetResult((new List<int>(), Array.Empty<int>()), 19, 0);
            parent.SetResult((new List<int>(), Array.Empty<int>()));
            Check(response.State == ResponseState.Valid && response.Error == null && response.ReturnCode == null && response.RowsAffected == null,
                "Base SetResult retained stale metadata");
            Throws<ArgumentOutOfRangeException>(() => response.SetResult(default, 0, -1));
            Check(!response.ToDictionary().ContainsKey("TotalResults"), "Response introduced an ambiguous global total");
        });
    }

    [SqlProcedure("MultiUsers")]
    private sealed class MultiUsers(BaseConnector connector = null) : SqlLinqerMultiCallable<(List<UserRow> Users, int[] Numbers)>(connector) { }
    [SqlProcedure("MultiUsers")]
    private sealed class MultiInputCall(BaseConnector connector = null) : SqlLinqerMultiCallable<MultiInput, (List<UserRow> Users, int[] Numbers)>(connector) { }
    private sealed class MultiInput { public SqlArgument<int?> Value { get; set; } }
    [SqlProcedure("ManyCollections")]
    private sealed class ManyCollections : SqlLinqerMultiCallable<(IReadOnlyList<UserRow> Users, HashSet<int> Set, Queue<int> Queue,
        ImmutableArray<int> Immutable, ReadOnlyCollection<int> ReadOnly, LinkedList<int> Linked, Collection<int> Collection, Stack<int> Stack, IEnumerable<int> Last)>
    { }
    [SqlProcedure("ThreeTables")]
    private sealed class ThreeTables : SqlLinqerMultiCallable<(int[] First, List<int> Middle, int[] Last)> { }
    [SqlProcedure("CustomTables")]
    private sealed class CustomTables : SqlLinqerMultiCallable<(List<UserRow> Users, CustomNumbers Numbers)> { }
    private sealed class CustomNumbers : List<int> { }
    private sealed class UnsupportedNumbers : List<int> { }
    [SqlProcedure("CollectionPair")]
    private sealed class CollectionPair<T> : SqlLinqerMultiCallable<(int[] First, T Second)> { }
    [SqlProcedure("ImmutableTables")]
    private sealed class ImmutableTables : SqlLinqerMultiCallable<(ImmutableRow[] Users, int?[] Numbers)> { }
    private sealed class SnapshotNumbers(IEnumerable<int> rows) : List<int>(rows) { }
    [SqlProcedure("SnapshotTables")]
    private sealed class SnapshotTables : SqlLinqerMultiCallable<(List<UserRow> Users, SnapshotNumbers Numbers)> { }
    [SqlProcedure("MultiTvp")]
    private sealed class MultiTvp : SqlLinqerMultiCallable<TvpInput, (List<UserRow> Users, int[] Numbers)> { }
    [SqlProcedure("Invalid")]
    private sealed class InvalidMulti<T> : SqlLinqerMultiCallable<T> { }
    private sealed class MissingMultiMapping : SqlLinqerMultiCallable<(int[], int[])> { }
}
