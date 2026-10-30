#nullable enable
using System.Data;
using System.Text.Json;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using SqlLinqer.Components.Native;
using SqlLinqer.Components.Select;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class NativeResultRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        void Async(string name, Func<Task> run) => test("native " + name, () => run().GetAwaiter().GetResult());
        foreach (var flavor in Enum.GetValues<DbFlavor>())
        {
            test("native scalar child DISTINCT stays in its parent query " + flavor, () =>
            {
                var query = Graph();
                query.Selects.GetAll<SelectRelationship>().Single(r => r.Alias == nameof(Root.Tags)).SubQuery.Distinct = true;
                using var compiled = query.Render(flavor);
                Check(!compiled.Text.Contains("CREATE TEMP", StringComparison.OrdinalIgnoreCase), "DISTINCT introduced temporary tables");
                Check(compiled.Text.Contains("DISTINCT") || compiled.NativePlan.Nodes.Any(n => n.LocalQuery?.Text.Contains("DISTINCT") == true), "Flat child DISTINCT was discarded");
            });
            test("native DISTINCT graph count uses server SQL " + flavor, () =>
            {
                var query = Graph().Distinct();
                using var count = query.Capture().RenderForCount(flavor);
                Check(count.NativePlan == null && count.Text.Contains("COUNT(*)"), "Graph count did not stay in SQL");
                Check(query.RenderForCount(new ParameterCollection(), flavor) == count.Text, "Count rendering differs");
            });
            test("native flat rendering is stable " + flavor, () =>
            {
                var query = new SelectQuery<Root>().SelectRootColumns().Where(x => x.Id, 7, SqlOp.EQ).Top(2);
                string before = query.Render(flavor).Text;
                var connector = Connector(new FakeDatabase(), flavor);
                var native = query.Capture().RenderForExecution<Root>(connector);
                Check(native.NativePlan != null && !native.NativePlan.IsGraph, "Expected a native flat plan");
                Check(query.Render(flavor).Text == before, "Native compilation changed public rendering");
                Check(native.Parameters.GetAll().Single().Value.Equals(7), "Bindings changed");
            });
        }
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql, DbFlavor.PostgreSql, DbFlavor.MySql, DbFlavor.DuckDb })
            test("native graph uses completion framing and detached bindings " + flavor, () =>
            {
                var query = Graph(); string before = query.Render(flavor).Text;
                var native = query.Capture().RenderForExecution<Root>(Connector(new(), flavor));
                Check(native.NativePlan?.IsGraph == true && native.NativePlan.Nodes.Length == 5, "Wrong graph plan");
                Check(native.Text.Contains(" OVER ()") && native.Text.Contains("__slq_count"), "Missing current-root completion framing");
                Check(!native.Text.Contains("JSON_", StringComparison.OrdinalIgnoreCase) && !native.Text.Contains("FOR JSON", StringComparison.OrdinalIgnoreCase), "Native graph still uses JSON");
                Check(query.Render(flavor).Text == before, "Parent bindings escaped compilation");
            });

        Async("explicit completion yields without a next-root read and cancels before disposal", async () =>
        {
            var database = new FakeDatabase(); var connector = Connector(database, DbFlavor.SqlServer);
            var query = Graph(); var rendered = query.Capture().RenderForExecution<Root>(connector); var plan = rendered.NativePlan!;
            database.ReaderFactory = _ => Records(plan).CreateDataReader();
            database.BeforeAsyncRead = _ => database.AsyncReads < 8 ? Task.CompletedTask : throw new InvalidOperationException("Read next root");
            Root? result = null;
            await foreach (var row in query.ExecuteAsyncStream(connector)) { result = row; break; }
            Check(database.AsyncReads == 8, "Root completion read ahead");
            var complete = result ?? throw new Exception("No root delivered");
            Check(complete.Id == 7 && complete.Orders.Count == 2 && complete.Orders[0].Items[0].Label == "first", "Nested positions changed");
            Check(complete.Orders[1].Id == 9 && complete.Tags.Select(t => t.Id).SequenceEqual(new[] { 5, 5 }), "Duplicate child occurrences lost");
            Check(complete.Profile != null && complete.Profile.Note == null, "All-NULL profile lost presence");
            Check(database.Executed.Single().CancelCalls == 1 && database.DisposedReaders == 1, "Early stop did not cancel and dispose");
        });

        foreach (string corruption in new[] { "truncated", "duplicate", "hole", "count", "position-overflow", "unknown-shape", "negative-count", "null-position" })
            Async("rejects corrupt graph without yielding partial root: " + corruption, async () =>
            {
                var database = new FakeDatabase(); var connector = Connector(database, DbFlavor.SqlServer); var query = Graph();
                var plan = query.Capture().RenderForExecution<Root>(connector).NativePlan!;
                database.ReaderFactory = _ =>
                {
                    var rows = Records(plan);
                    if (corruption == "truncated") rows.Rows.RemoveAt(rows.Rows.Count - 1);
                    if (corruption == "duplicate") rows.Rows[1].ItemArray = (object[])rows.Rows[0].ItemArray.Clone();
                    if (corruption == "hole") rows.Rows[0][3] = 3;
                    if (corruption == "count") rows.Rows[rows.Rows.Count - 1][2] = 100L;
                    if (corruption == "position-overflow") rows.Rows[0][3] = (long)int.MaxValue + 1;
                    if (corruption == "unknown-shape") rows.Rows[0][1] = int.MaxValue;
                    if (corruption == "negative-count") rows.Rows[rows.Rows.Count - 1][2] = -1L;
                    if (corruption == "null-position") rows.Rows[0][3] = DBNull.Value;
                    return rows.CreateDataReader();
                };
                int delivered = 0;
                try { await foreach (var row in query.ExecuteAsyncStream(connector)) delivered++; throw new Exception("Accepted corrupt graph"); }
                catch (SqlLinqer.Exceptions.SqlResponseException) { Check(delivered == 0, "Partial graph escaped"); }
                Check(database.Executed.Single().CancelCalls == 1, "Failed graph was not interrupted");
            });

        test("SQLite nested execution compiles a native local plan", () =>
        {
            var connector = Connector(new(), DbFlavor.Sqlite);
            Check(Graph().Capture().RenderForExecution<Root>(connector).NativePlan?.LocalQueries == true, "SQLite graph did not compile native local cursors");
        });

        test("native constructor-bound projections preserve missing defaults", () =>
        {
            var database = new FakeDatabase
            {
                ReaderFactory = _ =>
            { var t = new DataTable(); t.Columns.Add("Id", typeof(long)); t.Columns.Add("Name", typeof(string)); t.Rows.Add(7L, "雪"); return t.CreateDataReader(); }
            };
            var connector = Connector(database, DbFlavor.SqlServer);
            var query = new SelectQuery<Immutable>().Select(x => x.Id).Select(x => x.Name);
            var row = Result(query.Execute(connector)).Single();
            Check(row.Id == 7 && row.Name == "雪" && row.Missing == 42, "Native constructor or optional default differs");
        });
        test("native SQL member metadata ignores API JSON attributes and owns binary values", () =>
        {
            var bytes = new byte[] { 1, 2 };
            var database = new FakeDatabase
            {
                ReaderFactory = _ =>
            { var t = new DataTable(); t.Columns.Add("Id", typeof(int)); t.Columns.Add("Name", typeof(string)); t.Columns.Add("ReadOnly", typeof(string)); t.Columns.Add("Payload", typeof(byte[])); t.Rows.Add(1, "SQL", "loaded", bytes); t.Rows.Add(2, "next", "next", bytes); return t.CreateDataReader(); }
            };
            var connector = Connector(database, DbFlavor.PostgreSql);
            var rows = Result(new SelectQuery<PrivateModel>().Select(x => x.Id).Select(x => x.Name).Select(x => x.ReadOnly).Select(x => x.Payload).Execute(connector));
            Check(rows[0].Id == 1 && rows[0].Name == "SQL" && rows[0].ReadOnly == "loaded" && rows[0].Constructor == "private", "SQL metadata or private members differ");
            bytes[0] = 255; rows[0].Payload[1] = 254;
            Check(rows[0].Payload[0] == 1 && rows[1].Payload.SequenceEqual(new byte[] { 1, 2 }), "Native binary buffers are shared");
        });

        Async("real SQLite flat native constructor, field and conversion parity", async () =>
        {
            using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
            using (var cmd = c.CreateCommand())
            { cmd.CommandText = "CREATE TABLE native_roots(Id INTEGER,Name TEXT,ProfileId INTEGER);INSERT INTO native_roots VALUES(7,'雪😀',NULL),(8,NULL,1)"; cmd.ExecuteNonQuery(); }
            var connector = new ConnectionFactoryConnector(() => throw new Exception("Used a second connection"), DbFlavor.Sqlite, new() { });
            using var tx = c.BeginTransaction(); var query = new SelectQuery<Root>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC);
            var sync = Result(query.Execute(connector, tx)); var asyncRows = Result(await query.ExecuteAsync(connector, tx)); var stream = new List<Root>();
            await foreach (var row in query.ExecuteAsyncStream(connector, tx)) stream.Add(row);
            Check(sync.Count == 2 && sync[0].Name == "雪😀" && sync[1].Name == null, "Flat native values changed");
            Check(JsonSerializer.Serialize(sync) == JsonSerializer.Serialize(asyncRows) && JsonSerializer.Serialize(sync) == JsonSerializer.Serialize(stream), "Execution modes differ");
            tx.Rollback();
        });
        foreach (var flavor in new[] { DbFlavor.DuckDb, DbFlavor.Sqlite })
            Async("real " + flavor + " native graph is complete in all execution modes", async () =>
            {
                using System.Data.Common.DbConnection c = flavor == DbFlavor.DuckDb ? new DuckDBConnection("Data Source=:memory:") : new SqliteConnection("Data Source=:memory:"); c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = """
                CREATE TABLE native_roots(Id INTEGER,Name VARCHAR,ProfileId INTEGER);
                CREATE TABLE native_profiles(Id INTEGER,Note VARCHAR);
                CREATE TABLE native_orders(Id INTEGER,ParentId INTEGER,Title VARCHAR);
                CREATE TABLE native_items(Id INTEGER,ParentId INTEGER,Label VARCHAR);
                CREATE TABLE native_tags(Id INTEGER,ParentId INTEGER);
                INSERT INTO native_roots VALUES(7,'雪😀',1),(7,'duplicate',1),(8,NULL,NULL);
                INSERT INTO native_profiles VALUES(1,NULL);
                INSERT INTO native_orders VALUES(9,7,'one'),(10,7,'two');
                INSERT INTO native_items VALUES(11,9,'first'),(12,10,NULL);
                INSERT INTO native_tags VALUES(5,7),(5,7);
                """; cmd.ExecuteNonQuery();
                }
                var connector = new ConnectionFactoryConnector(() => throw new Exception("Used a second connection"), flavor, new() { });
                using var tx = c.BeginTransaction(); var query = SnapshotGraph.Capture(Graph().OrderBy(x => x.Name, SqlDir.ASC));
                query.Selects.GetAll<SelectRelationship>().Single(x => x.Alias == nameof(Root.Orders)).SubQuery.Table.Alias = "__slq_n0";
                var native = Result(query.Execute(connector, tx)); var asyncRows = Result(await query.ExecuteAsync(connector, tx)); var stream = new List<Root>();
                await foreach (var row in query.ExecuteAsyncStream(connector, tx)) stream.Add(row);

                Check(JsonSerializer.Serialize(native) == JsonSerializer.Serialize(asyncRows) && JsonSerializer.Serialize(native) == JsonSerializer.Serialize(stream), "Native execution modes differ");
                Check(native.Count == 3 && native.Where(r => r.Id == 7).All(r => r.Tags.Count == 2), "Duplicate roots or children lost"); tx.Rollback();
            });
    }

    internal static SelectQuery<Root> Graph() => new SelectQuery<Root>().SelectRootColumns().Select(x => x.Profile)
        .Select(x => x.Orders).Select(x => x.Orders.First().Items).Select(x => x.Tags).OrderByAuto();
    private static FakeConnector Connector(FakeDatabase database, DbFlavor flavor)
    { var c = new FakeConnector(database, flavor); return c; }
    private static List<T> Result<T>(SqlResponse<List<T>> r)
    { if (r.State != ResponseState.Valid) throw r.Error; return r.Result; }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    internal static DataTable Records(NativeResultPlan plan)
    {
        var table = new DataTable(); for (int i = 0; i < plan.FieldCount; i++) table.Columns.Add("c" + i, typeof(object));
        var root = plan.Root; var orders = root.Children.Single(n => n.Alias == nameof(Root.Orders)); var items = orders.Children.Single();
        var tags = root.Children.Single(n => n.Alias == nameof(Root.Tags)); var profile = root.Children.Single(n => n.Alias == nameof(Root.Profile));
        void Row(NativeResultNode node, int p, int item, params (string Name, object? Value)[] values)
        {
            var row = Enumerable.Repeat<object>(DBNull.Value, plan.FieldCount).ToArray(); row[0] = 1L; row[1] = node.Id; row[2] = node.Id == 0 ? 8L : DBNull.Value;
            for (int i = 0; i < plan.Depth; i++) row[3 + i] = 0L;
            if (node.Depth > 0) row[3] = (long)p; if (node.Depth > 1) row[4] = (long)item;
            for (int i = 0; i < node.Scalars.Length; i++)
            { var value = values.FirstOrDefault(v => v.Name == node.Scalars[i].Alias).Value; row[plan.ControlCount + node.Scalars[i].Ordinal] = value ?? DBNull.Value; }
            table.Rows.Add(row);
        }
        // Descendants, reverse positions, duplicate projected IDs, and parent/header last.
        Row(tags, 2, 0, ("Id", 5), ("ParentId", 7)); Row(items, 1, 1, ("Id", 11), ("ParentId", 9), ("Label", "first"));
        Row(orders, 2, 0, ("Id", 9), ("ParentId", 7), ("Title", "duplicate")); Row(tags, 1, 0, ("Id", 5), ("ParentId", 7));
        Row(orders, 1, 0, ("Id", 9), ("ParentId", 7), ("Title", "one")); Row(profile, 1, 0, ("Id", 1), ("Note", null));
        Row(items, 2, 1, ("Id", 12), ("ParentId", 9), ("Label", null)); Row(root, 0, 0, ("Id", 7), ("Name", "雪😀"), ("ProfileId", 1));
        return table;
    }

    [SqlTable("native_roots")]
    internal sealed class Root
    {
        [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; }
        public string? Name { get; set; }
        public int? ProfileId { get; set; }
        [SqlOneToOne(nameof(ProfileId))] public Profile? Profile { get; set; }
        [SqlOneToMany(nameof(Order.ParentId))] public List<Order> Orders { get; set; } = [];
        [SqlOneToMany(nameof(Tag.ParentId))] public List<Tag> Tags { get; set; } = [];
    }
    [SqlTable("native_profiles")]
    internal sealed class Profile { [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; } public string? Note { get; set; } }
    [SqlTable("native_orders")]
    internal sealed class Order
    {
        [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; }
        public int ParentId { get; set; }
        public string Title { get; set; } = "";
        [SqlOneToMany(nameof(Item.ParentId))] public List<Item> Items { get; set; } = [];
    }
    [SqlTable("native_items")]
    internal sealed class Item { [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; } public int ParentId { get; set; } public string? Label { get; set; } }
    [SqlTable("native_tags")]
    internal sealed class Tag { [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; } public int ParentId { get; set; } }
    [SqlTable("native_immutable")]
    private sealed class Immutable
    {
        [SqlPrimaryKey] public int Id { get; }
        public string Name { get; }
        public int Missing { get; }
        public Immutable(int id, string name, int missing = 42) { Id = id; Name = name; Missing = missing; }
    }
    [SqlTable("native_private")]
    private sealed class PrivateModel
    {
        [System.Text.Json.Serialization.JsonIgnore] public int Id { get; private set; }
        [System.Text.Json.Serialization.JsonPropertyName("api_name")] public string Name { get; private set; } = "";
        public string ReadOnly { get; } = "initial";
        public byte[] Payload { get; private set; } = [];
        [SqlIgnore] public string Constructor { get; } = "";
        private PrivateModel() { Constructor = "private"; }
        [System.Text.Json.Serialization.JsonConstructor] public PrivateModel(int id, string name) { Id = id; Name = name; Constructor = "api"; }
    }
}
