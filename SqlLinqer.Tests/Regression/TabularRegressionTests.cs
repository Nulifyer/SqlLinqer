using System.Data;
using System.Text.Json.Serialization;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class TabularRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
        {
            SqlResponse<List<T>> Read<T>(FakeConnector connector, RenderedQuery query = null,
                FakeTransaction transaction = null) => asynchronous
                ? connector.ExecuteReaderListAsync<T>(query ?? new RenderedQuery("SELECT rows"), transaction).GetAwaiter().GetResult()
                : connector.ExecuteReaderList<T>(query ?? new RenderedQuery("SELECT rows"), transaction);

            test($"tabular list maps SQL names, fields, nulls, enums, GUIDs and ignores API attributes, async={asynchronous}", () =>
            {
                var database = Database(Rows);
                var connector = new FakeConnector(database);
                var query = new RenderedQuery();
                query.Text = "SELECT rows WHERE Id = " + query.Parameters.AddParameter(7);
                var row = Result(Read<Model>(connector, query)).Single();
                Check(row.Id == 7 && row.Name == "雪" && row.Status == Status.Active && row.Optional == null
                    && row.Token == Guid.Parse("59dd2343-40a9-4adb-b99b-0c9f6bdf53ba")
                    && row.Payload.SequenceEqual(new byte[] { 0, 128, 255 }) && row.ApiOnly == null,
                    "Mapped values differ");
                Check((int)database.Executed.Single().Parameters[0].Value == 7, "Parameter was not bound");
                Check(database.Connections.Single().IsDisposed && database.Executed.Single().IsDisposed,
                    "Owned resources were not disposed");
                Check(asynchronous ? database.AsyncCommands == 1 && database.SyncCommands == 0 && database.DisposedReaders == 1
                    : database.SyncCommands == 1, "Execution style changed");
            });

            test($"tabular list maps immutable models and nullable scalars, async={asynchronous}", () =>
            {
                var connector = new FakeConnector(Database(Rows));
                var row = Result(Read<ImmutableModel>(connector)).Single();
                Check(row.Id == 7 && row.Name == "雪", "Constructor mapping failed");
                connector = new FakeConnector(Database(() =>
                {
                    var table = new DataTable(); table.Columns.Add("value", typeof(int));
                    table.Rows.Add(7); table.Rows.Add(DBNull.Value); return table;
                }));
                Check(Result(Read<long?>(connector)).SequenceEqual(new long?[] { 7, null }), "Nullable scalar mapping failed");
            });

            test($"tabular list returns empty rows with and without a result schema, async={asynchronous}", () =>
            {
                var connector = new FakeConnector(Database(() => { var table = Rows(); table.Clear(); return table; }));
                Check(Result(Read<Model>(connector)).Count == 0, "Empty result was not empty");
                Check(Result(Read<Model>(new FakeConnector(Database(() => new DataTable())))).Count == 0,
                    "Command without rows was not empty");
            });

            foreach (string failure in new[] { "missing", "null", "format", "overflow", "execute" })
                test($"tabular list returns no partial models after {failure} failure, async={asynchronous}", () =>
                {
                    var database = Database(() =>
                    {
                        var table = Rows();
                        if (failure == "missing") table.Columns.Remove("display_name");
                        if (failure == "null") table.Rows.Add(DBNull.Value, "second", 1, DBNull.Value,
                            "59dd2343-40a9-4adb-b99b-0c9f6bdf53ba", new byte[] { 1 }, "ignored");
                        if (failure == "format") table.Rows[0]["Token"] = "invalid-guid";
                        if (failure == "overflow") table.Rows[0]["ID"] = long.MaxValue;
                        return table;
                    });
                    if (failure == "execute") database.FailAt = "execute";
                    var response = Read<Model>(new FakeConnector(database));
                    Check(response.State == ResponseState.Error && response.Result == null && response.Error != null,
                        "Failure escaped the response contract or exposed partial models");
                    Check(database.Connections.Single().IsDisposed && database.Executed.Single().IsDisposed,
                        "Failed read leaked owned resources");
                });

            test($"tabular list preserves caller transaction after mapping failure, async={asynchronous}", () =>
            {
                var database = Database(() => { var table = Rows(); table.Columns.Remove("display_name"); return table; });
                var connector = new FakeConnector(database);
                using var connection = new FakeConnection(database); connection.Open();
                using var transaction = (FakeTransaction)connection.BeginTransaction();
                Check(Read<Model>(connector, transaction: transaction).State == ResponseState.Error, "Expected mapping failure");
                Check(connector.Created == 0 && !connection.IsDisposed && !transaction.IsDisposed
                    && transaction.Commits == 0 && transaction.Rollbacks == 0
                    && database.Executed.Single().UsedTransaction == transaction, "Caller ownership changed");
            });

            test($"tabular list mapping failure rolls back owned output transaction, async={asynchronous}", () =>
            {
                var database = Database(Rows);
                var connector = new FakeConnector(database, DbFlavor.DuckDb);
                var query = new UpdateQuery<WriteModel>().Update(x => x.Id, 7).Output(x => x.Id).Render(connector);
                var response = Read<MissingModel>(connector, query);
                var transaction = database.Connections.Single().Transactions.Single();
                Check(response.State == ResponseState.Error && transaction.Commits == 0
                    && transaction.Rollbacks == 1 && transaction.IsDisposed, "Mapping failure committed owned output");
            });

            foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
            test($"tabular table and list skip leading non-tabular results, flavor={flavor}, async={asynchronous}", () =>
            {
                var database = new FakeDatabase { ReaderFactory = _ => new DataTableReader(new[] { new DataTable(), Rows() }) };
                var connector = new FakeConnector(database, flavor);
                using var table = Result(asynchronous ? connector.ExecuteReaderDataTableAsync(new RenderedQuery("SELECT rows")).GetAwaiter().GetResult()
                    : connector.ExecuteReaderDataTable(new RenderedQuery("SELECT rows")));
                Check(table.Rows.Count == 1 && Result(Read<Model>(connector)).Single().Id == 7, "Leading result hid rows");
                if (asynchronous) Check(database.AsyncNextResults == 2, "Result advancement was not asynchronous");
            });
        }

        test("tabular async list propagates cancellation while loading and disposes resources", () =>
        {
            using var cancellation = new CancellationTokenSource();
            var database = Database(Rows);
            database.BeforeAsyncRead = _ => { cancellation.Cancel(); return Task.CompletedTask; };
            AsyncRegressionTests.Canceled(() => new FakeConnector(database).ExecuteReaderListAsync<Model>(
                new RenderedQuery("SELECT rows"), cancellationToken: cancellation.Token), cancellation.Token).GetAwaiter().GetResult();
            Check(database.DisposedReaders == 1 && database.Executed.Single().IsDisposed && database.Connections.Single().IsDisposed,
                "Cancellation leaked resources");
        });

        test("tabular async list observes cancellation during model mapping", () =>
        {
            using var cancellation = new CancellationTokenSource();
            CancelModel.OnConstruct = cancellation.Cancel;
            try
            {
                var database = Database(Rows);
                AsyncRegressionTests.Canceled(() => new FakeConnector(database).ExecuteReaderListAsync<CancelModel>(
                    new RenderedQuery("SELECT rows"), cancellationToken: cancellation.Token), cancellation.Token).GetAwaiter().GetResult();
                Check(database.Connections.Single().IsDisposed && database.DisposedReaders == 1, "Mapping cancellation leaked resources");
            }
            finally { CancelModel.OnConstruct = null; }
        });
    }

    private static FakeDatabase Database(Func<DataTable> table) => new() { ReaderFactory = _ => table().CreateDataReader() };

    private static DataTable Rows()
    {
        var table = new DataTable();
        table.Columns.Add("ID", typeof(long)); table.Columns.Add("display_name", typeof(string));
        table.Columns.Add("Status", typeof(int)); table.Columns.Add("Optional", typeof(int));
        table.Columns.Add("Token", typeof(string)); table.Columns.Add("Payload", typeof(byte[]));
        table.Columns.Add("Extra", typeof(string));
        table.Rows.Add(7L, "雪", 1, DBNull.Value, "59dd2343-40a9-4adb-b99b-0c9f6bdf53ba", new byte[] { 0, 128, 255 }, "ignored");
        return table;
    }

    private static T Result<T>(SqlResponse<T> response)
    {
        Check(response.State == ResponseState.Valid, response.Error?.ToString());
        return response.Result;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private enum Status { Active = 1 }
    private sealed class Model
    {
        public int Id { get; set; }
        [SqlColumn("display_name"), JsonIgnore] public string Name { get; set; }
        public Status Status = default;
        public int? Optional { get; set; }
        public Guid Token { get; set; }
        public byte[] Payload { get; set; }
        [SqlIgnore] public string ApiOnly { get; set; }
    }
    private sealed class ImmutableModel(int id, string name)
    {
        public int Id { get; } = id;
        [SqlColumn("display_name")] public string Name { get; } = name;
    }
    private sealed class MissingModel { public string Missing { get; set; } }
    [SqlTable("items")]
    private sealed class WriteModel { public int Id { get; set; } }
    private sealed class CancelModel
    {
        internal static Action OnConstruct;
        public CancelModel() { OnConstruct?.Invoke(); }
        public int Id { get; set; }
    }
}
