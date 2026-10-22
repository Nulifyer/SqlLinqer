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
    internal static void Run(Action<string, Action> test)
    {
        foreach (var flavor in Enum.GetValues<DbFlavor>())
            foreach (string mode in new[] { "sync", "async", "stream" })
                foreach (bool empty in new[] { false, true })
                {
                    test($"review framework collection SQL loading {flavor}, {mode}, empty={empty}", () =>
                    {
                        string values = empty ? "[]" : "[{\"Id\":7}]";
                        string dictionary = empty ? "{}" : "{\"one\":{\"Id\":7}}";
                        string json = $"{{\"Array\":{values},\"List\":{values},\"ReadOnly\":{values},\"Dictionary\":{dictionary},\"ImmutableDictionary\":{dictionary},\"Set\":{values},\"Interface\":{values}}}";
                        var db = new FakeDatabase { JsonRows = [flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "[" + json + "]" : json] };
                        var connector = new FakeConnector(db, flavor);
                        var row = Read<Collections>(connector, new("JSON"), mode).Single();
                        int count = empty ? 0 : 1;
                        Check(row.Array.Length == count && row.List.Count == count && row.ReadOnly.Count == count && row.Dictionary.Count == count && row.ImmutableDictionary.Count == count && row.Set.Count == count && row.Interface.Count == count, "Collection shape differs");
                        if (!empty) Check(row.Array[0].Id == 7 && row.List[0].Id == 7 && row.ReadOnly[0].Id == 7 && row.Dictionary["one"].Id == 7, "API attributes changed SQL members");
                    });
                }

        foreach (string mode in new[] { "sync", "async", "stream" })
        {
            test($"review SQLite hex decoding and JSON member matching, {mode}", () =>
            {
                var bytes = Enumerable.Range(0, 4096).Select(x => (byte)x).ToArray();
                foreach (string hex in new[] { Convert.ToHexString(bytes), Convert.ToHexString(bytes).ToLowerInvariant(), "", "006\\u0031FF" })
                {
                    var connector = new FakeConnector(new() { JsonRows = [$"{{\"data\":\"{hex}\",\"k\\u0065Y\":7}}"] }, DbFlavor.Sqlite);
                    var row = Read<BinaryRow>(connector, new("JSON"), mode).Single();
                    var expected = hex == "" ? [] : hex.Contains("\\u") ? new byte[] { 0, 97, 255 } : bytes;
                    Check(row.Data.SequenceEqual(expected) && row.Key == 7, "Hex or escaped/case-insensitive member matching differs");
                }
                foreach (string hex in new[] { "0", "0G", "é0", "00\\u0058F" })
                {
                    var connector = new FakeConnector(new() { JsonRows = [$"{{\"Data\":\"{hex}\"}}"] }, DbFlavor.Sqlite);
                    try { Read<BinaryRow>(connector, new("JSON"), mode); throw new Exception("Invalid hex was accepted"); }
                    catch (SqlLinqer.Exceptions.SqlResponseException error) { Check(error.GetBaseException() is JsonException, "Invalid hex lost its JSON error"); }
                }
                var nullConnector = new FakeConnector(new() { JsonRows = ["{\"Data\":null}"] }, DbFlavor.Sqlite);
                Check(Read<BinaryRow>(nullConnector, new("JSON"), mode).Single().Data == null, "SQL NULL binary changed");
            });
        }
        test("review segmented hex input decodes without retaining source storage", () =>
        {
            byte[] json = Encoding.UTF8.GetBytes("\"00aaFF\"");
            var first = new Segment(json.AsMemory(0, 4));
            var last = first.Append(json.AsMemory(4));
            var reader = new Utf8JsonReader(new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length));
            Check(reader.Read() && reader.HasValueSequence, "Fixture did not split a string token");
            var type = typeof(BaseConnector).Assembly.GetType("SqlLinqer.Components.Json.DatabaseHexBytesConverter", true);
            var converter = (JsonConverter<byte[]>)Activator.CreateInstance(type, true);
            byte[] result = converter.Read(ref reader, typeof(byte[]), new JsonSerializerOptions());
            Array.Fill(json, (byte)'0');
            Check(result.SequenceEqual(new byte[] { 0, 170, 255 }), "Segmented decoding retained source memory");
        });

        test("SQL string conversion preserves segmented numeric tokens", () =>
        {
            const string token = "1.234500000000000000000000000000000e+400";
            byte[] json = Encoding.UTF8.GetBytes(token);
            var first = new Segment(json.AsMemory(0, 10));
            var last = first.Append(json.AsMemory(10));
            var reader = new Utf8JsonReader(new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length));
            Check(reader.Read() && reader.HasValueSequence, "Fixture did not split a numeric token");
            var type = typeof(BaseConnector).Assembly.GetType("SqlLinqer.Components.Json.DatabaseStringConverter", true);
            var converter = (JsonConverter<string>)Activator.CreateInstance(type, true);
            string result = converter.Read(ref reader, typeof(string), new JsonSerializerOptions());
            Array.Fill(json, (byte)'0');
            Check(result == token, "Numeric text lost precision, formatting, or ownership");
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
                    Renamed.BeginSelect(connector), Renamed.BeginInsert(connector).InsertAuto(new Renamed { Key = 7 }),
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
        if (mode == "sync") return Result(connector.ExecuteReaderJson<T>(query));
        if (mode == "async") return Result(connector.ExecuteReaderJsonAsync<T>(query).GetAwaiter().GetResult());
        async Task<List<T>> Collect() { var rows = new List<T>(); await foreach (var row in connector.ExecuteReaderJsonAsyncStream<T>(query)) rows.Add(row); return rows; }
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
