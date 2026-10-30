using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class SqlServerNativeShapeChecks
{
    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        if (connector.DbFlavor is not (DbFlavor.SqlServer or DbFlavor.AzureSql)) return;
        foreach (string type in new[] { "nvarchar(max)", "ntext", "varchar(max)", "text", "xml", "varbinary(max)", "image", "float", "datetime2(7)" })
            test("native DISTINCT preserves SQL Server scalar types: " + type, () => Scalars(connector, type).GetAwaiter().GetResult());
        test("native DISTINCT preserves a seventeen-level finite relationship", () => Deep(connector).GetAwaiter().GetResult());
    }

    private static async Task Scalars(BaseConnector connector, string type)
    {
        string schema = connector.Options.DefaultSchema;
        string T(string name) => string.IsNullOrEmpty(schema) ? name.DbWrap(connector.DbFlavor)
            : schema.DbWrap(connector.DbFlavor) + "." + name.DbWrap(connector.DbFlavor);
        using var connection = connector.CreateConnection(); await connection.OpenAsync();
        async Task Sql(string text) { using var command = connection.CreateCommand(); command.CommandText = text; await command.ExecuteNonQueryAsync(); }
        bool roots = false, children = false;
        try
        {
            await Sql($"CREATE TABLE {T("native_lob_roots")}(Id int,Name nvarchar(20)); INSERT INTO {T("native_lob_roots")} VALUES(1,'same'),(2,'same'),(3,'same')"); roots = true;
            bool binary = type is "varbinary(max)" or "image";
            string collation = type is "text" or "ntext" or "nvarchar(max)" or "varchar(max)" ? " COLLATE Latin1_General_100_CI_AS" : "";
            await Sql($"CREATE TABLE {T("native_lob_children")}(Id int,ParentId int,Value {type}{collation})"); children = true;
            string[] values = binary ? ["0x0102", "0x0102", "0x0304"] : type == "xml" ? ["N'<v>a</v>'", "N'<v>A</v>'", "N'<v>b</v>'"]
                : type == "float" ? ["1.0000000000000002", "1.0000000000000004", "1.0000000000000006"]
                : type == "datetime2(7)" ? ["'2026-01-01T00:00:00.0000001'", "'2126-01-01T00:00:00.0000001'", "'2026-01-01T00:00:00.0000002'"]
                : ["N'a'", "N'A'", "N'b'"];
            await Sql($"INSERT INTO {T("native_lob_children")} VALUES(1,1,{values[0]}),(2,2,{values[1]}),(3,3,{values[2]})");
            using var tx = connection.BeginTransaction();
            int expected = 3;
            foreach (string mode in new[] { "sync", "async", "stream" })
            {
                var query = new SelectQuery<LobRoot>(schema).Select(x => x.Name).Select(x => x.Children).Distinct();
                var child = query.Selects.GetAll<SelectRelationship>().Single().SubQuery;
                child.Selects.Clear(); child.Selects.AddStatement(new SelectColumn(child.Table.Columns.TryGet(nameof(LobChild.Value)), nameof(LobChild.Value)));
                List<LobRoot> rows;
                if (mode == "stream") { rows = []; await foreach (var row in query.ExecuteAsyncStream(connector, tx)) rows.Add(row); }
                else rows = (mode == "sync" ? query.Execute(connector, tx) : await query.ExecuteAsync(connector, tx)).GetResultOrThrow();
                Expect(rows.Count == expected && rows.All(r => r.Children.Count == 1), "Native scalar equality differs, " + type + ", " + mode);
                Expect(rows.All(r => binary ? r.Children[0].Value is byte[] data && data.Length == 2
                    : type == "float" ? r.Children[0].Value is double
                    : type == "datetime2(7)" ? r.Children[0].Value is DateTime : r.Children[0].Value is string), "Provider scalar type changed");
            }
            tx.Rollback();
        }
        finally
        {
            if (children) await Sql($"DROP TABLE {T("native_lob_children")}");
            if (roots) await Sql($"DROP TABLE {T("native_lob_roots")}");
        }
    }

    private static async Task Deep(BaseConnector connector)
    {
        string schema = connector.Options.DefaultSchema;
        string table = string.IsNullOrEmpty(schema) ? "[native_deep_nodes]" : schema.DbWrap(connector.DbFlavor) + ".[native_deep_nodes]";
        using var connection = connector.CreateConnection(); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = $"CREATE TABLE {table}(Id int,OwnerId int,Name nvarchar(20))"; await command.ExecuteNonQueryAsync();
        try
        {
            command.CommandText = $"INSERT INTO {table} VALUES" + string.Join(",", Enumerable.Range(1, 18).Select(i => $"({i},{(i == 18 ? "NULL" : (i + 1).ToString())},'n{i}')"));
            await command.ExecuteNonQueryAsync();
            using var tx = connection.BeginTransaction();
            foreach (string mode in new[] { "sync", "async", "stream" })
            {
                var query = new SelectQuery<DeepNode>(schema).SelectRootColumns().Where(x => x.Id, 1, SqlOp.EQ).Distinct();
                SqlLinqer.Queries.Core.SelectQuery current = query;
                for (int depth = 0; depth < 17; depth++)
                {
                    var relation = new SelectRelationship(current, current.Table.Relationships.TryGet(nameof(DeepNode.Owner)));
                    foreach (var column in relation.SubQuery.Table.Columns.GetAll()) relation.SubQuery.Selects.AddStatement(new SelectColumn(column, column.Alias));
                    current.Selects.AddStatement(relation); current = relation.SubQuery;
                }
                List<DeepNode> rows;
                if (mode == "stream") { rows = []; await foreach (var row in query.ExecuteAsyncStream(connector, tx)) rows.Add(row); }
                else rows = (mode == "sync" ? query.Execute(connector, tx) : await query.ExecuteAsync(connector, tx)).GetResultOrThrow();
                Expect(rows.Count == 1, "Wrong deep root count"); var value = rows[0];
                for (int id = 1; id <= 18; id++)
                { Expect(value != null && value.Id == id && value.Name == "n" + id, "Incomplete deep node " + id); value = value.Owner; }
                Expect(value == null, "Unexpected deep tail");
            }
            tx.Rollback();
        }
        finally { command.Transaction = null; command.CommandText = $"DROP TABLE {table}"; await command.ExecuteNonQueryAsync(); }
    }

    private static void Expect(bool value, string message) { if (!value) throw new Exception(message); }
    [SqlTable("native_lob_roots")]
    private sealed class LobRoot
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        [SqlOneToMany(nameof(LobChild.ParentId))] public List<LobChild> Children { get; set; } = [];
    }
    [SqlTable("native_lob_children")]
    private sealed class LobChild
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int ParentId { get; set; }
        public object Value { get; set; }
    }
    [SqlTable("native_deep_nodes")]
    private sealed class DeepNode
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int? OwnerId { get; set; }
        public string Name { get; set; }
        [SqlOneToOne(nameof(OwnerId))] public DeepNode Owner { get; set; }
    }
}
