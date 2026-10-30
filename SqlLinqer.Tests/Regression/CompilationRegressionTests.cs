using System.Data;
using SqlLinqer;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

internal static class CompilationRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var flavor in Enum.GetValues<DbFlavor>())
            test("native DISTINCT shares CTE bindings across node sources " + flavor, () =>
            {
                var query = NativeResultRegressionTests.Graph().Distinct()
                    .Where(x => x.Orders.First().Id, Enumerable.Range(1, 900).ToArray(), SqlArrayOp.IN);
                using var first = query.Render(flavor);
                using var second = query.Render(flavor);
                Check(first.Parameters.Count <= 910, "CTE bindings multiplied with graph depth");
                Check(first.Text == second.Text, "Native resource names changed reusable SQL");
                Check(first.Parameters.GetAll().Count(p => p.Value is int value && value is >= 1 and <= 900) == 900,
                    "CTE input values were lost or duplicated");
            });
        test("SELECT base string rendering does not recursively dispatch overrides", () =>
        {
            var query = new TaggedSelect().SelectRootColumns();
            var parameters = new ParameterCollection(DbFlavor.PostgreSql);
            string sql = query.Render(parameters, DbFlavor.PostgreSql);
            Check(sql.Split("-- string extension").Length == 2, "String override reentered");
        });
        test("typed SELECT compilation hook preserves layout, counts and snapshot isolation", () =>
        {
            var query = new CompiledSelect().SelectRootColumns().Where(x => x.Name, "name", SqlOp.EQ).Page(1, 2);
            var snapshot = query.Capture();
            string before = snapshot.Render(DbFlavor.PostgreSql).Text;
            var count = snapshot.RenderForCount(DbFlavor.PostgreSql);
            Check(count.Text.Contains("COUNT(") && count.Text.EndsWith("-- count extension"), "Count hook received data SQL");
            Check(snapshot.Render(DbFlavor.PostgreSql).Text == before && before.EndsWith("-- data extension"), "Hook mutated captured state");
            Check(count.Parameters.Count == 1 && snapshot.Render(DbFlavor.PostgreSql).NativePlan != null, "Hook lost parameters or result plan");
        });
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
            test($"group count omits measures and preserves DISTINCT/overall aggregates for {flavor}", () =>
            {
                var grouped = new SelectQueryInitializer<Row>().BuildQuery(q => new
                {
                    Name = q.Select(x => x.Name),
                    Total = q.Sum(x => x.Id)
                }).Page(2, 3);
                string data = grouped.Render(flavor).Text;
                string count = grouped.Capture().RenderForCount(flavor).Text;
                Check(count.Contains("GROUP BY") && !count.Contains("SUM("), "Group count evaluates unused measure");
                Check(grouped.Render(flavor).Text == data, "Count changed data projection");
                Check(grouped.Distinct().Capture().RenderForCount(flavor).Text.Contains("SUM("), "DISTINCT count lost projected values");
                var overall = new SelectQueryInitializer<Row>().BuildQuery(q => new { Total = q.Sum(x => x.Id) });
                Check(overall.Capture().RenderForCount(flavor).Text.Contains("SUM("), "Overall aggregate lost its one-row semantics");
                var bound = new MeasureQuery().Where(x => x.Name, "selected", SqlOp.EQ).Capture().RenderForCount(flavor);
                Check(bound.Parameters.GetAll().Count() == 1 && bound.Parameters.GetAll().Single().Value.Equals("selected"),
                    "Omitted measure retained an unused binding or lost the filter");
            });
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
    private sealed class TaggedSelect : SelectQuery<Row>
    {
        public override string Render(ParameterCollection parameters, DbFlavor flavor)
            => base.Render(parameters, flavor) + " -- string extension";
    }
    private sealed class CompiledSelect : SelectQuery<Row>
    {
        protected internal override RenderedQuery CompileSelect(DbFlavor flavor, bool count)
        {
            QueryName = "working state";
            var result = base.CompileSelect(flavor, count);
            result.Text += count ? "-- count extension" : "-- data extension";
            return result;
        }
    }
    private sealed class MeasureQuery : SelectQuery<Row>
    {
        internal MeasureQuery()
        {
            Select(x => x.Name);
            Selects.AddStatement(new SelectFuncStatement(SqlFunc.COALESCE, "Total",
                new SelectFuncStatement(SqlFunc.SUM, "Total", Table.PrimaryKey), 7));
        }
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
