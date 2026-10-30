using System.Data;
using System.Data.Common;
using SqlLinqer.Components.Native;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class MappingOwnershipChecks
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (string alias in new[] { "Key", "db_key", "DB_KEY" })
            test("constructor member binding resolves CLR and SQL names: " + alias, () =>
            {
                var row = (OwnedRoot)new NativeModelBinding(typeof(OwnedRoot), new[] { alias }).Construct(new object[] { 7 });
                Check(row.Key == 7 && row.Derived == 14 && row.OptionalValue == 42, "Constructor input/defaults differ");
            });
        foreach (string alias in new[] { "Key", "db_key", "DB_KEY" })
            test("tabular constructor uses the shared SQL alias contract: " + alias, () =>
            {
                using var table = new DataTable();
                table.Columns.Add(alias, typeof(int)); table.Columns.Add("OptionalValue", typeof(int)); table.Columns.Add("Data", typeof(byte[]));
                table.Rows.Add(7, 5, new byte[] { 1 });
                using var reader = table.CreateDataReader(); reader.Read();
                var mapping = SqlLinqer.Components.Modeling.TabularRowType.GetCached(typeof(OwnedRoot));
                var row = (OwnedRoot)mapping.Read(reader, mapping.GetOrdinals(reader));
                Check(row.Key == 7 && row.Derived == 14 && row.OptionalValue == 5, "Tabular constructor alias binding differs");
            });
    }

    internal static void RunProvider(BaseConnector source, Action<string, Action> test)
        => test("mapping ownership: constructor aliases, binary DISTINCT and streamed mutation", () => Provider(source).GetAwaiter().GetResult());

    private static async Task Provider(BaseConnector source)
    {
        var flavor = source.DbFlavor; string schema = source.Options.DefaultSchema;
        string Q(string name) => name.DbWrap(flavor);
        string T(string name) => string.IsNullOrEmpty(schema) ? Q(name) : Q(schema) + "." + Q(name);
        string binary = flavor switch { DbFlavor.SqlServer or DbFlavor.AzureSql => "varbinary(16)", DbFlavor.PostgreSql => "bytea", _ => "blob" };
        using var connection = source.CreateConnection(); await connection.OpenAsync();
        var created = new List<string>();
        async Task Sql(string sql, byte[] data = null)
        {
            using var command = connection.CreateCommand(); command.CommandText = sql;
            if (flavor == DbFlavor.DuckDb) command.CommandText = command.CommandText.Replace("@data", "$data");
            if (data != null) { var p = command.CreateParameter(); p.ParameterName = flavor == DbFlavor.DuckDb ? "data" : "@data"; p.DbType = DbType.Binary; p.Value = data; command.Parameters.Add(p); }
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            await Sql($"CREATE TABLE {T("review_owned_roots")} ({Q("db_key")} int,{Q("OptionalValue")} int,{Q("Data")} {binary})"); created.Add("review_owned_roots");
            await Sql($"CREATE TABLE {T("review_owned_children")} ({Q("db_child")} int,{Q("parent_key")} int,{Q("Data")} {binary})"); created.Add("review_owned_children");
            foreach (int id in new[] { 7, 7, 8 }) await Sql($"INSERT INTO {T("review_owned_roots")} VALUES({id},5,@data)", new byte[] { (byte)id });
            foreach (int id in new[] { 10, 10, 11 }) await Sql($"INSERT INTO {T("review_owned_children")} VALUES({id},7,@data)", new byte[] { (byte)id });
            var connector = new ConnectionFactoryConnector(source.CreateConnection, flavor, new() { DefaultSchema = schema });
            using var transaction = connection.BeginTransaction();
            SelectQuery<OwnedRoot> Query(bool distinct = true)
            {
                var q = new SelectQuery<OwnedRoot>(schema).SelectRootColumns().Select(r => r.Children).OrderBy(r => r.Key, SqlDir.ASC);
                if (distinct) q.Distinct();
                q.Selects.GetAll<SelectRelationship>().Single().SubQuery.Distinct = true;
                return q;
            }
            void Verify(List<OwnedRoot> rows)
            {
                Check(rows.Count == 2 && rows[0].Key == 7 && rows[1].Key == 8, "Wrong DISTINCT roots");
                Check(rows[0].Derived == 14 && rows[0].OptionalValue == 5 && rows[1].Derived == 16, "Wrong root constructor values");
                Check(rows[0].Children.Count == 2 && rows[0].Children.All(c => c.Derived == c.Id * 2 && c.ParentId == 7), "Wrong nested constructor/DISTINCT values");
            }
            Verify(Query().Execute(connector, transaction).GetResultOrThrow());
            Verify((await Query().ExecuteAsync(connector, transaction)).GetResultOrThrow());
            var streamed = new List<OwnedRoot>();
            await foreach (var row in Query().ExecuteAsyncStream(connector, transaction))
            {
                streamed.Add(row);
                row.Data[0] = 255;
                foreach (var child in row.Children) child.Data[0] = 254;
            }
            Verify(streamed);
            var page = Query(); page.Limit(2, 0);
            var paged = new List<OwnedRoot>();
            await foreach (var row in page.ExecuteAsyncStream(connector, transaction)) { paged.Add(row); row.Data[0] = 253; }
            Verify(paged);
            var count = Query().Capture().RenderForCount(flavor);
            Check(connector.ExecuteScalar<long>(count, transaction).GetResultOrThrow() == 2, "Wrong graph DISTINCT count");
            var partial = new SelectQuery<OwnedRoot>(schema).Select(r => r.Key).OrderBy(r => r.Key, SqlDir.ASC);
            Check(partial.Execute(connector, transaction).GetResultOrThrow().All(r => r.Derived == r.Key * 2 && r.OptionalValue == 42), "Omitted constructor default changed");
            transaction.Rollback();
        }
        finally { foreach (string name in created.AsEnumerable().Reverse()) await Sql("DROP TABLE " + T(name)); }
    }

    [SqlTable("review_owned_roots")]
    internal sealed class OwnedRoot
    {
        [SqlPrimaryKey, SqlColumn("db_key")] public int Key { get; }
        public int OptionalValue { get; }
        public byte[] Data { get; set; }
        [SqlIgnore] public int Derived { get; }
        [SqlOneToMany(nameof(OwnedChild.ParentId))] public List<OwnedChild> Children { get; set; } = new();
        public OwnedRoot(int db_key, int optionalValue = 42)
        { if (db_key <= 0) throw new ArgumentOutOfRangeException(nameof(db_key)); Key = db_key; Derived = db_key * 2; OptionalValue = optionalValue; }
    }
    [SqlTable("review_owned_children")]
    internal sealed class OwnedChild
    {
        [SqlPrimaryKey, SqlColumn("db_child")] public int Id { get; }
        [SqlColumn("parent_key")] public int ParentId { get; }
        public byte[] Data { get; set; }
        [SqlIgnore] public int Derived { get; }
        public OwnedChild(int db_child, int parent_key)
        { if (db_child <= 0 || parent_key <= 0) throw new ArgumentOutOfRangeException(); Id = db_child; ParentId = parent_key; Derived = db_child * 2; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
