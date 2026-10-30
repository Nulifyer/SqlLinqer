using System.Collections.Immutable;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Data;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

internal static class ReviewRegressionTests
{
    private class CopyPlanBase : SqlLinqer.Queries.Core.SelectQuery
    {
        private byte[] shared = new byte[] { 7 };
        public byte[] Shared => shared;
        public readonly List<object> Readonly;
        protected CopyPlanBase() : base(new SqlLinqer.Components.Modeling.Table("schema", "rows"))
        { Readonly = new List<object> { shared }; }
    }
    private sealed class CopyPlanQuery : CopyPlanBase { public object Link; }
    internal static void Run(Action<string, Action> test)
    {
        test("snapshot field plans copy inherited private and readonly state with cycles", () =>
        {
            var source = new CopyPlanQuery(); source.Link = source;
            Parallel.For(0, 64, _ =>
            {
                var copy = SqlLinqer.Compilation.SnapshotGraph.Capture(source);
                Check(ReferenceEquals(copy.Link, copy), "Query cycle escaped its snapshot");
                Check(ReferenceEquals(copy.Shared, copy.Readonly[0]), "Private/readonly fields lost shared identity");
                copy.Shared[0] = 8;
                Check(source.Shared[0] == 7 && copy.Readonly.Count == 1, "Compiled field copy retained mutable state");
            });
        });
        test("snapshot collection factories preserve comparers, aliases and cycles", () =>
        {
            var bytes = new byte[] { 1, 2 };
            var list = new List<object> { bytes, bytes }; list.Add(list);
            var source = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["Items"] = list };
            var copy = SqlLinqer.Compilation.SnapshotGraph.Capture(source);
            var copied = (List<object>)copy["ITEMS"];
            Check(ReferenceEquals(copy.Comparer, source.Comparer), "Dictionary comparer changed");
            Check(!ReferenceEquals(copied, list) && !ReferenceEquals(copied[0], bytes), "Snapshot retained mutable values");
            Check(ReferenceEquals(copied[0], copied[1]) && ReferenceEquals(copied[2], copied), "Shared references or collection cycles changed");
            ((byte[])copied[0])[0] = 9;
            Check(bytes[0] == 1, "Captured array mutation escaped");
        });
        test("snapshot collection factories isolate concurrent copies", () =>
        {
            var source = new Dictionary<string, List<byte[]>>(StringComparer.OrdinalIgnoreCase) { ["Values"] = new() { new byte[] { 7 } } };
            Parallel.For(0, 128, i =>
            {
                var copy = SqlLinqer.Compilation.SnapshotGraph.Capture(source);
                copy["VALUES"][0][0] = 8;
                Check(source["values"][0][0] == 7 && copy["VALUES"].Count == 1, "Concurrent snapshots shared mutable storage");
            });
        });
        foreach (var type in new[] { typeof(Child[]), typeof(List<Child>), typeof(ReadOnlyCollection<Child>), typeof(IReadOnlyList<Child>), typeof(ImmutableArray<Child>), typeof(ImmutableList<Child>), typeof(ImmutableHashSet<Child>) })
            foreach (bool empty in new[] { true, false })
                test($"native collection construction {type}, empty={empty}", () =>
                {
                    var values = empty ? new List<object>() : new List<object> { new Child { Id = 7 } };
                    var binding = new SqlLinqer.Components.Native.NativeCollectionBinding(type);
                    var result = (IEnumerable<Child>)binding.Build(values);
                    Check(type.IsInstanceOfType(result) && result.Count() == (empty ? 0 : 1), "Native collection changed its declared type or elements");
                });
        foreach (bool asynchronous in new[] { false, true })
        {
            test($"review cached native mapping isolates concurrent readers, async={asynchronous}", () =>
            {
                Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(async () =>
                {
                    using var table = new DataTable();
                    if (index % 2 == 0) { table.Columns.Add("db_key", typeof(int)); table.Columns.Add("Name", typeof(string)); }
                    else { table.Columns.Add("Name", typeof(string)); table.Columns.Add("Key", typeof(int)); }
                    for (int row = 0; row < 3; row++)
                    {
                        int key = index * 3 + row;
                        if (index % 2 == 0) table.Rows.Add(key, $"row-{key}");
                        else table.Rows.Add($"row-{key}", key);
                    }
                    var connector = new FakeConnector(new() { ReaderFactory = _ => table.CreateDataReader() });
                    var response = asynchronous ? await connector.ExecuteReaderListAsync<ConcurrentRow>(new("rows"))
                        : connector.ExecuteReaderList<ConcurrentRow>(new("rows"));
                    var rows = Result(response);
                    Check(rows.Count == 3 && rows.Select((row, offset) => row.Key == index * 3 + offset && row.Name == $"row-{row.Key}").All(x => x),
                        "Cached mapping shared ordinals or scratch values across readers");
                }))).GetAwaiter().GetResult();
            });

            test($"review native aliases and temporal scalars, async={asynchronous}", () =>
            {
                SqlResponse<List<T>> ReadRows<T>(DataTable table) => asynchronous
                    ? new FakeConnector(new() { ReaderFactory = _ => table.CreateDataReader() }).ExecuteReaderListAsync<T>(new("rows")).GetAwaiter().GetResult()
                    : new FakeConnector(new() { ReaderFactory = _ => table.CreateDataReader() }).ExecuteReaderList<T>(new("rows"));
                using var names = new DataTable(); names.Columns.Add("Key", typeof(int)); names.Rows.Add(7);
                Check(Result(ReadRows<Renamed>(names)).Single().Key == 7, "CLR projection alias was rejected");
                names.Columns[0].ColumnName = "db_key";
                Check(Result(ReadRows<Renamed>(names)).Single().Key == 7, "SQL column name was rejected");
                names.Columns.Add("Key", typeof(int)); names.Rows[0][1] = 8;
                Check(ReadRows<Renamed>(names).State == ResponseState.Error, "Ambiguous SQL and CLR names were accepted");
                using var constructorRows = new DataTable(); constructorRows.Columns.Add("db_key", typeof(int));
                constructorRows.Rows.Add(1); constructorRows.Rows.Add(2);
                var immutable = Result(ReadRows<ImmutableRenamed>(constructorRows));
                Check(immutable[0].Key == 1 && immutable[1].Key == 2 && !ReferenceEquals(immutable[0], immutable[1]), "Constructor mapping reused a previous result or its scratch values");
                names.Columns.RemoveAt(1); names.Columns.Add("DB_KEY", typeof(int));
                Check(ReadRows<Renamed>(names).State == ResponseState.Error, "Case-insensitive duplicate columns were accepted");
                using var dates = new DataTable(); dates.Columns.Add("Value", typeof(DateOnly)); dates.Rows.Add(new DateOnly(2024, 2, 29)); dates.Rows.Add(DBNull.Value);
                Check(Result(ReadRows<DateOnly?>(dates)).SequenceEqual(new DateOnly?[] { new(2024, 2, 29), null }), "Native DateOnly scalar list differs");
                using var times = new DataTable(); times.Columns.Add("Value", typeof(TimeOnly)); times.Rows.Add(new TimeOnly(12, 34, 56));
                Check(Result(ReadRows<TimeOnly>(times)).Single() == new TimeOnly(12, 34, 56), "Native TimeOnly scalar list differs");
            });

            foreach (string stage in new[] { "commit", "dispose-command", "dispose-transaction", "dispose-connection", "success" })
                test($"review disposable result ownership, async={asynchronous}, stage={stage}", () =>
                {
                    var value = new DisposableValue();
                    var connector = new FakeConnector(new() { FailAt = stage, ScalarFactory = () => value });
                    var response = asynchronous ? connector.ExecuteScalarAsync<object>(new("scalar")).GetAwaiter().GetResult()
                        : connector.ExecuteScalar<object>(new("scalar"));
                    Check(stage == "success" ? response.State == ResponseState.Valid && value.Disposals == 0
                        : response.State == ResponseState.Error && value.Disposals == 1, "Result disposal differs");
                    if (stage == "success") value.Dispose();
                });

            test($"review DuckDB generated default inserts and caller rollback, async={asynchronous}", () =>
            {
                using var keeper = new DuckDBConnection("Data Source=:memory:"); keeper.Open();
                var connector = new ConnectionFactoryConnector(() => new DuckDBConnection("Data Source=:memory:"), DbFlavor.DuckDb);
                using var transaction = keeper.BeginTransaction();
                Result(connector.ExecuteNonQuery(new("CREATE SEQUENCE review_ids; CREATE TABLE review_defaults(Id INTEGER PRIMARY KEY DEFAULT nextval('review_ids'), Twice INTEGER GENERATED ALWAYS AS (Id*2))"), transaction));
                var query = new InsertWithPrimaryKeyQuery<DefaultRow, int>().InsertAuto(new[] { new DefaultRow(), new DefaultRow() });
                var response = asynchronous ? query.ExecuteAsync(connector, transaction).GetAwaiter().GetResult() : query.Execute(connector, transaction);
                Check(Result(response) == 2 && response.TouchedPrimaryKeys.Distinct().Count() == 2, "Default batch keys differ");
                var rows = Result(new SelectQuery<DefaultRow>().SelectRootColumns().Execute(connector, transaction));
                Check(rows.All(x => x.Twice == x.Id * 2), "Generated column differs");
                transaction.Rollback();
                Check(keeper.State == ConnectionState.Open, "Caller connection closed");
            });
        }

        test("review explicit connector null schema never inherits global schema", () =>
        {
            var previous = Default.Connector;
            try
            {
                var global = new FakeConnector(new()); global.Options.DefaultSchema = "other_tenant";
                Default.Connector = global;
                var connector = new FakeConnector(new(), DbFlavor.Sqlite);
                var queries = new SqlLinqer.Queries.Core.Abstract.BaseQuery[]
                {
                    Renamed.BeginSelect(connector).SelectRootColumns(), Renamed.BeginInsert(connector).InsertAuto(new Renamed { Key = 7 }),
                    Renamed.BeginUpdate(connector).Update(x => x.Key, 8), Renamed.BeginDelete(connector),
                    Renamed.BeginPrimaryKeyExists(7, connector)
                };
                foreach (var query in queries) Check(!query.Render(DbFlavor.SqlServer).Text.Contains("other_tenant"), "Global schema leaked into an explicit connector");
                Check(!Renamed.BeginSelectBuilder(connector).BuildQuery(x => new { Key = x.Select(y => y.Key) }).Render(DbFlavor.Sqlite).Text.Contains("other_tenant"), "Projection schema leaked");
            }
            finally { Default.Connector = previous; }
        });

        test("review TVP decimal transport preserves scale and integer range", () =>
        {
            foreach (var values in new[] { new[] { 0.1234567890123456789012345678m }, new[] { decimal.MaxValue, -decimal.MaxValue, 0.0000000000000000000000000000m }, new[] { 1.25m, 12345.6789m } })
            {
                var query = new InsertQuery<DecimalRow>().InsertAuto(values.Select(x => new DecimalRow { Value = x }));
                int scale = values.Max(x => x == 0 ? 0 : (decimal.GetBits(x)[3] >> 16) & 255);
                Check(query.RenderTempType(DbFlavor.SqlServer, "probe").Create.Contains($"DECIMAL(38,{scale})"), "Generated scale differs from values");
            }
            var incompatible = new InsertQuery<DecimalRow>().InsertAuto(new[] { new DecimalRow { Value = decimal.MaxValue }, new DecimalRow { Value = 0.0000000000000000000000000001m } });
            var connector = new FakeConnector(new(), DbFlavor.SqlServer);
            try { incompatible.ExecuteTvp(connector); throw new Exception("Unrepresentable batch was accepted"); }
            catch (InvalidOperationException error) { Check(error.Message.Contains("decimal"), "Missing precision explanation"); }
            try { incompatible.ExecuteTvpAsync(connector).GetAwaiter().GetResult(); throw new Exception("Unrepresentable async batch was accepted"); }
            catch (InvalidOperationException error) { Check(error.Message.Contains("decimal"), "Missing async precision explanation"); }
            Check(connector.Created == 0, "Unrepresentable batch opened a connection");
        });

        test("review TVP decimal tables support SqlClient metadata inference", () =>
        {
            var metadataType = typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly
                .GetType("Microsoft.Data.SqlClient.Server.MetaDataUtilsSmi", true);
            var infer = metadataType.GetMethod("SmiMetaDataFromDataColumn", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var values in new decimal?[][]
            {
                [0.1234567890123456789012345678m, null],
                [decimal.MaxValue, -decimal.MaxValue, 0.0000000000000000000000000000m, null],
                [1.2300000000000000000000000000m, 2m, null],
                [null, null]
            })
            {
                if (values.Contains(decimal.MaxValue))
                {
                    using var raw = new DataTable(); raw.Columns.Add("Value", typeof(decimal));
                    foreach (var value in values) raw.Rows.Add(value.HasValue ? (object)value.Value : DBNull.Value);
                    try { infer.Invoke(null, [raw.Columns[0], raw]); throw new Exception("Fixture did not reproduce SqlClient's excessive precision"); }
                    catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
                }
                var parameters = new ParameterCollection(DbFlavor.SqlServer);
                new InsertQuery<NullableDecimalRow>().InsertAuto(values.Select(value => new NullableDecimalRow { Value = value }))
                    .RenderTVP(parameters, DbFlavor.SqlServer, null);
                using var table = (DataTable)parameters.GetAll().OfType<TvpParameter>().Single().Value;
                Check(infer.Invoke(null, [table.Columns[0], table]) != null, "SqlClient could not infer decimal metadata");
                Check(table.Rows.Cast<DataRow>().Select(row => row.IsNull(0) ? (decimal?)null : (decimal)row[0]).SequenceEqual(values),
                    "Decimal transport normalization changed values or nulls");
            }
        });
    }

    private static List<T> Read<T>(BaseConnector connector, RenderedQuery query, string mode) where T : class
    {
        if (mode == "sync") return Result(connector.ExecuteReaderModel<T>(query));
        if (mode == "async") return Result(connector.ExecuteReaderModelAsync<T>(query).GetAwaiter().GetResult());
        async Task<List<T>> Collect() { var rows = new List<T>(); await foreach (var row in connector.ExecuteReaderModelAsyncStream<T>(query)) rows.Add(row); return rows; }
        return Collect().GetAwaiter().GetResult();
    }
    private static T Result<T>(SqlResponse<T> response) => response.State == ResponseState.Valid ? response.Result : throw response.Error;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Collections
    {
        public ImmutableDictionary<string, Child> ImmutableDictionary { get; set; }
        public ImmutableHashSet<Child> Set { get; set; }
        public IReadOnlyList<Child> Interface { get; set; }
        public ImmutableArray<Child> Array { get; set; }
        public ImmutableList<Child> List { get; set; }
        public ReadOnlyCollection<Child> ReadOnly { get; set; }
        public ReadOnlyDictionary<string, Child> Dictionary { get; set; }
    }
    private sealed class Child { [JsonIgnore] public int Id { get; set; } }
    [SqlTable("review_defaults")]
    private sealed class DefaultRow { [SqlPrimaryKey] public int Id { get; set; } [SqlAutoInsertExclude] public int Twice { get; set; } }
    private sealed class Renamed : SqlLinqerPrimaryKeyObject<Renamed, int> { [SqlPrimaryKey(false), SqlColumn("db_key")] public int Key { get; set; } }
    private sealed class ImmutableRenamed(int key) { [SqlColumn("db_key")] public int Key { get; } = key; }
    private sealed class ConcurrentRow(int key, string name) { [SqlColumn("db_key")] public int Key { get; } = key; public string Name { get; } = name; }
    private sealed class DecimalRow { public decimal Value { get; set; } }
    private sealed class NullableDecimalRow { public decimal? Value { get; set; } }
    private sealed class BinaryRow { public byte[] Data { get; set; } public int Key { get; set; } }
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory) { Memory = memory; }
        internal Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
    private sealed class DisposableValue : IDisposable { internal int Disposals; public void Dispose() => Disposals++; }
}
