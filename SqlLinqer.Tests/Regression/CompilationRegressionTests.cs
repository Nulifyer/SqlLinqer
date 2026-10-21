using System.Data;
using SqlLinqer;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class CompilationRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("capture freezes model aliases, builder paging, and predicates", () =>
        {
            var table = new Table("app", "original", "u");
            var column = new Column(table, "Id", "Id");
            table.Columns.AddColumn(column);
            var query = new SelectQuery<Row>(table).SelectRootColumns().Where(new SqlLinqer.Components.Where.WhereColumnValue(column, 7, SqlOp.EQ, SqlSubQueryOp.ANY)).Page(2, 5);
            var snapshot = query.Capture();
            string before = snapshot.Render(DbFlavor.PostgreSql).Text;
            table.Alias = "changed";
            column.ColumnName = "changed_column";
            query.Top(1).Where(new SqlLinqer.Components.Where.WhereColumnValue(column, 8, SqlOp.GT, SqlSubQueryOp.ANY));
            var after = snapshot.Render(DbFlavor.PostgreSql);
            Check(after.Text == before && after.Parameters.GetAll().Count() == 1, "Snapshot retained mutable builder/model state");
            Check(query.Render(DbFlavor.PostgreSql).Text != before, "Builder mutation did not take effect");
        });

        test("capture owns binary predicates and rendered bindings", () =>
        {
            byte[] bytes = [1, 2, 3];
            var snapshot = new SelectQuery<Row>().SelectRootColumns().Where(x => x.Payload, bytes, SqlOp.EQ).Capture();
            bytes[0] = 9;
            var first = snapshot.Render(DbFlavor.PostgreSql);
            var value = (byte[])first.Parameters.GetAll().Single().Value;
            Check(value.SequenceEqual(new byte[] { 1, 2, 3 }), "Input buffer leaked into snapshot");
            value[0] = 8;
            Check(((byte[])snapshot.Render(DbFlavor.PostgreSql).Parameters.GetAll().Single().Value)[0] == 1,
                "Rendered binding mutated future renders");
        });

        test("capture owns stored procedure TVP values", () =>
        {
            using var values = new DataTable();
            values.Columns.Add("Id", typeof(int)); values.Rows.Add(3);
            var snapshot = new StoredProcedureQuery<Row>("probe").AddTvpParameter("ids", values, "dbo.Ids").Capture();
            values.Rows[0][0] = 99;
            using var first = (DataTable)snapshot.Render(DbFlavor.SqlServer).Parameters.GetAll().Single().Value;
            Check((int)first.Rows[0][0] == 3, "TVP data was not captured");
            first.Rows[0][0] = 42;
            using var next = (DataTable)snapshot.Render(DbFlavor.SqlServer).Parameters.GetAll().Single().Value;
            Check((int)next.Rows[0][0] == 3, "TVP bindings were reused");
        });

        test("capture isolates read-only TVP binary cells", () =>
        {
            using var values = new DataTable();
            var column = values.Columns.Add("Payload", typeof(byte[]));
            byte[] bytes = [1, 2]; values.Rows.Add((object)bytes); column.ReadOnly = true;
            var snapshot = new StoredProcedureQuery<Row>("probe").AddTvpParameter("rows", values, "dbo.Payloads").Capture();
            bytes[0] = 9;
            using var captured = (DataTable)snapshot.Render(DbFlavor.SqlServer).Parameters.GetAll().Single().Value;
            Check(captured.Columns[0].ReadOnly && ((byte[])captured.Rows[0][0])[0] == 1, "Read-only TVP capture lost cell isolation or metadata");
        });

        test("insert capture reads mapped values once and isolates entity mutations", () =>
        {
            var row = new Row { Name = "original", Payload = [1, 2] };
            var query = new InsertQuery<Row>().InsertAuto(row);
            var snapshot = query.Capture();
            row.Name = "changed"; row.Payload[0] = 9;
            query.InsertAuto(new Row { Name = "extra" });
            var rendered = snapshot.Render(DbFlavor.PostgreSql);
            var values = rendered.Parameters.GetAll().Select(p => p.Value).ToArray();
            Check(values.Contains("original") && !values.Contains("changed") && !values.Contains("extra"), "Entity or row-list mutation leaked");
            Check(values.OfType<byte[]>().Single().SequenceEqual(new byte[] { 1, 2 }), "Entity buffer was retained");
        });

        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
        {
            test($"capture supports repeated data and count compilation for {flavor}", () =>
            {
                var query = new SelectQuery<Parent>().SelectRootColumns().Select(x => x.Children)
                    .Where(x => x.Children.First().Name, "child", SqlOp.EQ).Page(2, 3);
                var snapshot = query.Capture();
                string data = snapshot.Render(flavor).Text;
                string count = snapshot.RenderForCount(flavor).Text;
                Check(snapshot.Render(flavor).Text == data && snapshot.RenderForCount(flavor).Text == count,
                    "Count changed the captured data definition");
                Check(query.Render(flavor).Text == data, "Compilation mutated the builder");
                var rendered = snapshot.Render(flavor);
                Check(rendered.ResultShape.Members.Single(x => x.Name == nameof(Parent.Children)).Kind == SqlResultMemberKind.Collection,
                    "Nested collection shape was lost");
            });
        }

        test("direct write rendering isolates compilation and preserves virtual overrides", () =>
        {
            var query = new TaggedUpdate().Update(x => x.Name, "updated").Where(x => x.Id, 7, SqlOp.EQ);
            string expected = query.Render(DbFlavor.PostgreSql).Text;
            Check(expected.Split("-- custom").Length == 2, "Virtual rendering ran twice");
            Parallel.For(0, 30, _ =>
            {
                var parameters = new ParameterCollection();
                Check(query.Render(parameters, DbFlavor.PostgreSql) == expected && parameters.Count == 2, "Direct write rendering shared working state");
            });
        });

        test("one snapshot compiles concurrently across dialects with independent bindings", () =>
        {
            var snapshot = new SelectQuery<Row>().SelectRootColumns().Where(x => x.Name, "name", SqlOp.EQ).Page(2, 4).Capture();
            var flavors = Enum.GetValues<DbFlavor>();
            var expected = flavors.ToDictionary(f => f, f => snapshot.Render(f).Text);
            Parallel.For(0, 60, i =>
            {
                var flavor = flavors[i % flavors.Length];
                var rendered = snapshot.Render(flavor);
                Check(rendered.Text == expected[flavor], "Concurrent compilation changed SQL");
                Check(rendered.Parameters.GetAll().Single().Value.Equals("name"), "Binding changed");
                snapshot.RenderForCount(flavor);
                rendered.Text = "caller mutation";
            });
        });
    }

    private sealed class TaggedUpdate : UpdateQuery<Row>
    {
        public override string Render(ParameterCollection parameters, DbFlavor flavor)
            => base.Render(parameters, flavor) + " -- custom";
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    [SqlTable("snapshot_rows"), SqlAutoInsert]
    internal sealed class Row
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        public byte[] Payload { get; set; }
    }
    [SqlTable("snapshot_parents")]
    internal sealed class Parent
    {
        [SqlPrimaryKey] public int Id { get; set; }
        [SqlOneToMany(nameof(Child.ParentId))] public List<Child> Children { get; set; }
    }
    [SqlTable("snapshot_children")]
    internal sealed class Child
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; }
    }
}
