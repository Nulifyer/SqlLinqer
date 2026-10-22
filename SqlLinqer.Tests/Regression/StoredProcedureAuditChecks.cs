using System.Data;
using System.Reflection;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    private static void RunAudit(Action<string, Action> test)
    {
        test("procedure public API exposes only the attribute based two model callable", () =>
        {
            var exported = typeof(SqlLinqerCallable<,>).Assembly.GetExportedTypes();
            var callables = exported.Where(t => t.Name.StartsWith("SqlLinqerCallable`", StringComparison.Ordinal)).ToArray();
            Check(callables.Length == 1 && callables[0] == typeof(SqlLinqerCallable<,>), "More than one callable interface is exported");
            // These names are a denylist: retired interfaces must not reappear in the package.
            var retired = new[] { "StoredProcedureQuery", "CallableInvocation", "IQueryTypedStoredProcedure",
                "NamedParameterCollection", "SqlOutputParameter", "SqlResultSet", "SqlReturnValue", "SqlStoredProcedureResponse" };
            Check(!exported.Any(t => retired.Contains(t.Name.Split('`')[0])), "A retired procedure type is still exported");
            Check(typeof(SqlCallableResponse<UserRow>).BaseType == typeof(SqlResponse<List<UserRow>>), "Callable response changed its row result contract");
            Check(!typeof(SqlLinqerObject<>).GetMethods().Any(m => m.Name == "BeginStoredProcedure"), "Retired procedure factory remains");
            var constructor = typeof(SqlLinqerCallable<,>).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            Check(constructor.GetParameters() is [{ IsOptional: true } parameter]
                && parameter.ParameterType == typeof(SqlLinqer.Connections.BaseConnector), "Procedure configuration still requires constructor plumbing");
        });
        test("procedure async execution propagates tokens and closes reader before collecting outputs", () =>
        {
            var database = MetadataDatabase(22L);
            using var cancellation = new CancellationTokenSource();
            var response = PagedUsers().ExecuteAsync(new ExternalArgs(), new FakeConnector(database, DbFlavor.SqlServer),
                cancellationToken: cancellation.Token).GetAwaiter().GetResult();
            Check(response.State == ResponseState.Valid && response.Result.Single().Name == "雪"
                && response.TotalResults == 22 && response.ReturnCode == 17, response.Error?.ToString());
            Check(database.AsyncCommands == 1 && database.AsyncNextResults == 1 && database.AsyncReads > 0
                && database.SyncCommands == 0 && database.LastOpenToken == cancellation.Token
                && database.LastCommandToken == cancellation.Token && database.LastReadToken == cancellation.Token
                && database.LastNextToken == cancellation.Token && database.DisposedReaders == 1,
                "Async execution lost token propagation or reader cleanup");
        });
        foreach (bool asynchronous in new[] { false, true })
        {
            foreach (string stage in new[] { "open", "command", "parameter", "begin", "execute", "commit",
                "dispose-command", "dispose-connection", "dispose-transaction" })
                test($"procedure handles connector failure at {stage}, async={asynchronous}", () =>
                {
                    var database = InputDatabase(); database.FailAt = stage;
                    var connector = new FakeConnector(database, DbFlavor.SqlServer);
                    var response = asynchronous ? PagedUsers().ExecuteAsync(new ExternalArgs(), connector).GetAwaiter().GetResult()
                        : PagedUsers().Execute(new ExternalArgs(), connector);
                    Check(response.State == ResponseState.Error && response.Result == null && response.TotalResults == null
                        && response.ReturnCode == null && response.RowsAffected == null, "Connector failure retained output");
                    Check(database.Connections.Single().IsDisposed && database.Connections.Single().Commands.All(c => c.IsDisposed),
                        "Connector failure leaked owned resources");
                });
            test($"procedure skips update counts and preserves complete row mapping, async={asynchronous}", () =>
            {
                var database = new FakeDatabase
                {
                    ReaderFactory = command => new ClosingReader(new DataTableReader(new[] { new DataTable(), Users(), new DataTable() }),
                        () => SetReturnCode(command, 0))
                };
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                var response = asynchronous ? PagedUsers().ExecuteAsync(new ExternalArgs(), connector).GetAwaiter().GetResult()
                    : PagedUsers().Execute(new ExternalArgs(), connector);
                Check(response.State == ResponseState.Valid && response.Result.Single().Status == UserStatus.Active
                    && response.Result[0].Optional == null && response.Result[0].ApiOnly == null, response.Error?.ToString());
            });
            test($"procedure mapping failure preserves caller transaction, async={asynchronous}", () =>
            {
                var database = new FakeDatabase { ReaderFactory = _ => Users(nullId: true).CreateDataReader() };
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                using var connection = new FakeConnection(database); connection.Open();
                using var transaction = (FakeTransaction)connection.BeginTransaction();
                var response = asynchronous ? PagedUsers().ExecuteAsync(new ExternalArgs(), connector, transaction).GetAwaiter().GetResult()
                    : PagedUsers().Execute(new ExternalArgs(), connector, transaction);
                Check(response.State == ResponseState.Error && !connection.IsDisposed && !transaction.IsDisposed
                    && transaction.Commits == 0 && transaction.Rollbacks == 0 && connector.Created == 0,
                    "Mapping failure changed caller ownership");
            });
            foreach (string shape in new[] { "missing", "extra", "null-id", "missing-column", "ambiguous-column" })
                test($"procedure invalid {shape} shape clears rows and rolls back, async={asynchronous}", () =>
                {
                    bool closed = false;
                    var database = new FakeDatabase
                    {
                        ReaderFactory = command =>
                        {
                            var rows = shape == "missing" ? new DataTable() : Users(nullId: shape == "null-id");
                            if (shape == "missing-column") rows.Columns.Remove("display_name");
                            if (shape == "ambiguous-column") rows.Columns.Add("Name", typeof(string));
                            var reader = shape == "extra" ? new DataTableReader(new[] { rows, Users() }) : rows.CreateDataReader();
                            return new ClosingReader(reader, () => { closed = true; SetReturnCode(command, 0); });
                        }
                    };
                    var connector = new FakeConnector(database, DbFlavor.SqlServer);
                    var response = asynchronous ? PagedUsers().ExecuteAsync(new ExternalArgs(), connector).GetAwaiter().GetResult()
                        : PagedUsers().Execute(new ExternalArgs(), connector);
                    Check(response.State == ResponseState.Error && response.Result == null && response.TotalResults == null
                        && response.ReturnCode == null && response.RowsAffected == null, "Invalid shape leaked output");
                    Check(database.Connections.Single().IsDisposed && closed
                        && database.Connections.Single().Transactions.Single().Rollbacks == 1, "Invalid shape leaked owned resources");
                });
            test($"procedure maps immutable output and nullable scalar rows, async={asynchronous}", () =>
            {
                var database = InputDatabase();
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                var immutable = new ImmutableProcedure();
                var response = asynchronous ? immutable.ExecuteAsync(new EmptyInput(), connector).GetAwaiter().GetResult()
                    : immutable.Execute(new EmptyInput(), connector);
                Check(response.State == ResponseState.Valid && response.Result.Single().Name == "雪", response.Error?.ToString());
                database.ReaderFactory = command =>
                {
                    var table = new DataTable(); table.Columns.Add("Value", typeof(int)); table.Rows.Add(DBNull.Value);
                    return new ClosingReader(table.CreateDataReader(), () => SetReturnCode(command, 0));
                };
                var scalar = new NullableScalarProcedure();
                var nullable = asynchronous ? scalar.ExecuteAsync(new EmptyInput(), connector).GetAwaiter().GetResult()
                    : scalar.Execute(new EmptyInput(), connector);
                Check(nullable.State == ResponseState.Valid && nullable.Result.Single() == null, nullable.Error?.ToString());
            });
        }
        test("procedure async execution snapshots mutable TVP and readonly binary cells", () =>
        {
            using var table = new DataTable();
            table.Columns.Add("Id", typeof(int)); table.Columns.Add("Bytes", typeof(byte[]));
            table.Rows.Add(7, new byte[] { 1, 2 }); table.Columns["Bytes"].ReadOnly = true;
            var database = InputDatabase();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            database.BeforeAsyncCommand = _ => gate.Task;
            var procedure = new TvpProcedure();
            var input = new TvpInput { Ids = table };
            var rendered = procedure.Render(input, DbFlavor.SqlServer);
            Check(ReferenceEquals(rendered.Parameters.GetAll().Single(p => p.Value is DataTable).Value, table), "Render changed TVP identity");
            var task = procedure.ExecuteAsync(input, new FakeConnector(database, DbFlavor.SqlServer));
            try { table.Rows[0]["Id"] = 99; ((byte[])table.Rows[0]["Bytes"])[0] = 9; }
            finally { gate.SetResult(); }
            var response = task.GetAwaiter().GetResult();
            Check(response.State == ResponseState.Valid, response.Error?.ToString());
            using var frozen = (DataTable)Parameter(database.Executed.Single(), "Ids").Value;
            Check(!ReferenceEquals(frozen, table) && (int)frozen.Rows[0]["Id"] == 7
                && ((byte[])frozen.Rows[0]["Bytes"])[0] == 1 && frozen.Columns["Bytes"].ReadOnly,
                "Async execution observed TVP mutations after capture");
        });
        test("procedure rejects unsupported providers before opening connections", () =>
        {
            foreach (var flavor in new[] { DbFlavor.PostgreSql, DbFlavor.MySql, DbFlavor.Sqlite, DbFlavor.DuckDb })
            {
                var connector = new FakeConnector(InputDatabase(), flavor);
                Throws<NotSupportedException>(() => PagedUsers().Execute(new ExternalArgs(), connector));
                Throws<NotSupportedException>(() => PagedUsers().ExecuteAsync(new ExternalArgs(), connector).GetAwaiter().GetResult());
                Check(connector.Created == 0, "Unsupported provider opened a connection");
            }
        });
        test("procedure validates input metadata before opening connections", () =>
        {
            Invalid(new BadName()); Invalid(new DuplicateNames()); Invalid(new BadSize()); Invalid(new BadPrecision());
            Invalid(new BadScale()); Invalid(new MissingTableType()); Invalid(new InvalidStructured()); Invalid(new BadTemporalScale());
            Invalid(new BadDbType()); Invalid(new BadSqlDbType());
            void Invalid<T>(T input) where T : class
            {
                var connector = new FakeConnector(InputDatabase(), DbFlavor.SqlServer);
                Throws<InvalidOperationException>(() => new ValidationProcedure<T>().Execute(input, connector));
                Check(connector.Created == 0, "Invalid metadata opened a connection");
            }
        });
        test("procedure input mappings preserve ordering enums fields and ignored members", () =>
        {
            var database = InputDatabase();
            var procedure = new MappedInputProcedure();
            var input = new MappedInput();
            var sql = procedure.Render(input, DbFlavor.SqlServer);
            var azure = procedure.Render(input, DbFlavor.AzureSql);
            Check(sql.Text == azure.Text && sql.Text.IndexOf("@first =", StringComparison.Ordinal)
                < sql.Text.IndexOf("@Last =", StringComparison.Ordinal), "SQL flavor or explicit parameter order differs");
            var response = procedure.Execute(input, new FakeConnector(database, DbFlavor.SqlServer));
            Check(response.State == ResponseState.Valid && (int)Parameter(database.Executed.Single(), "first").Value == 1
                && (int)Parameter(database.Executed.Single(), "Last").Value == 9
                && database.Executed.Single().Parameters.Count == 4, response.Error?.ToString());
        });
    }

    [SqlProcedure("TvpProbe")]
    private sealed class TvpProcedure : SqlLinqerCallable<TvpInput, UserRow> { }
    private sealed class TvpInput { [SqlParameter(TypeName = "dbo.Ids")] public DataTable Ids { get; set; } }
    [SqlProcedure("Immutable")]
    private sealed class ImmutableProcedure : SqlLinqerCallable<EmptyInput, ImmutableRow> { }
    public sealed class ImmutableRow(string name) { [SqlColumn("display_name")] public string Name { get; } = name; }
    [SqlProcedure("Scalar")]
    private sealed class NullableScalarProcedure : SqlLinqerCallable<EmptyInput, int?> { }
    [SqlProcedure("Invalid")]
    private sealed class ValidationProcedure<T> : SqlLinqerCallable<T, UserRow> where T : class { }
    private sealed class BadName { [SqlParameter("invalid-name")] public int Value { get; set; } }
    private sealed class DuplicateNames { [SqlParameter("VALUE")] public int First { get; set; } public int Value { get; set; } }
    private sealed class BadSize { [SqlParameter(Size = -2)] public string Value { get; set; } }
    private sealed class BadPrecision { [SqlParameter(Precision = 39)] public decimal Value { get; set; } }
    private sealed class BadScale { [SqlParameter(Precision = 2, Scale = 3)] public decimal Value { get; set; } }
    private sealed class MissingTableType { public DataTable Value { get; set; } }
    private sealed class InvalidStructured { [SqlParameter(TypeName = "dbo.Ids")] public int Value { get; set; } }
    private sealed class BadTemporalScale { [SqlParameter(SqlDbType = SqlDbType.DateTime2, Scale = 8)] public DateTime Value { get; set; } }
    private sealed class BadDbType { [SqlParameter(DbType = (DbType)int.MaxValue)] public int Value { get; set; } }
    private sealed class BadSqlDbType { [SqlParameter(SqlDbType = (SqlDbType)int.MaxValue)] public int Value { get; set; } }
    [SqlProcedure("Mapped")]
    private sealed class MappedInputProcedure : SqlLinqerCallable<MappedInput, UserRow> { }
    private sealed class MappedInput
    {
        [SqlParameter("@first", Order = -1)] public UserStatus Status => UserStatus.Active;
        public int Last = 9;
        [SqlIgnore] public object Ignored => throw new Exception("Ignored member was read");
    }
}
