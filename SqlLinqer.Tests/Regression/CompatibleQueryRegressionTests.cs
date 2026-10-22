using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

internal static class CompatibleQueryRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
        {
            test($"parallel independent queries isolate bindings and schemas for {flavor}", () =>
            {
                Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                {
                    var query = new SelectQuery<Order>($"parallel_tenant_{i % 4}").Select(x => x.Id)
                        .Select(x => x.Billing.Name).Select(x => x.Shipping.Name)
                        .Where(x => x.Id, i, SqlOp.GT).Where(x => x.Billing.Name, $"billing-{i}", SqlOp.EQ).Page(2, 7);
                    var first = query.Render(flavor);
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        var next = query.Render(flavor);
                        Check(next.Text == first.Text, "Concurrent rendering changed SQL");
                        Check(next.Parameters.GetAll().Select(x => x.Value).SequenceEqual(new object[] { i, $"billing-{i}" }),
                            "Concurrent rendering contaminated parameter values");
                        var count = new ParameterCollection();
                        query.RenderForCount(count, flavor);
                        Check(count.GetAll().Select(x => x.Value).SequenceEqual(new object[] { i, $"billing-{i}" }),
                            "Concurrent count changed bindings");
                    }
                    Check(Table.GetCached<Order>($"parallel_tenant_{i % 4}").FindRelationshipFromPath(new[] { nameof(Order.Billing) })
                        .ChildTable.Schema == $"parallel_tenant_{i % 4}", "Concurrent table cache crossed schemas");
                });
            });
            test($"independent relationship occurrences and schemas for {flavor}", () =>
            {
                var table = Table.GetCached<Order>("tenant_a");
                var billing = table.FindRelationshipFromPath(new[] { nameof(Order.Billing) });
                var shipping = table.FindRelationshipFromPath(new[] { nameof(Order.Shipping) });
                Check(billing.ChildTable.UUID != shipping.ChildTable.UUID, "Different relationship members share an identity");
                Check(billing.ChildTable.Schema == "tenant_a" && shipping.ChildTable.Schema == "tenant_a", "Default schema did not propagate");
                var query = new FlatOrderQuery(table);
                string sql = query.Render(flavor).Text;
                Check(Regex.Matches(sql, "LEFT JOIN").Count == 2, "Two occurrences collapsed to one join");
                Check(sql.Contains(billing.ChildTable.Alias) && sql.Contains(shipping.ChildTable.Alias), "An occurrence is missing");
                Check(Table.GetCached<Order>("tenant_b").FindRelationshipFromPath(new[] { nameof(Order.Billing) }).ChildTable.Schema == "tenant_b", "Tenant schemas contaminated one another");
            });
            test($"relationship schema defaults honor explicit overrides for {flavor}", () =>
            {
                var table = Table.GetCached<SchemaRoot>("tenant_a");
                Check(table.Schema == "root_schema", "Explicit root schema lost");
                Check(table.FindRelationshipFromPath(new[] { nameof(SchemaRoot.Children) }).ChildTable.Schema == "tenant_a", "Root override replaced the configured default");
                Check(table.FindRelationshipFromPath(new[] { nameof(SchemaRoot.Fixed) }).ChildTable.Schema == "shared", "Explicit child schema lost");
                var direct = table.FindRelationshipFromPath(new[] { nameof(SchemaRoot.Tags) });
                Check(direct.Joins.First().Child.Schema == "tenant_a" && direct.ChildTable.Schema == "tenant_a", "Many-to-many defaults lost");
                Check(table.FindRelationshipFromPath(new[] { nameof(SchemaRoot.FixedTags) }).Joins.First().Child.Schema == "links", "Explicit linking schema lost");
                var typed = table.FindRelationshipFromPath(new[] { nameof(SchemaRoot.TypedTags) });
                Check(typed.Joins.First().Child.Schema == "tenant_a" && typed.ChildTable.Schema == "tenant_a", "Typed linking defaults lost");
            });
            test($"paging preserves grouped and distinct projections for {flavor}", () =>
            {
                var grouped = new SelectQueryInitializer<Order>().BuildQuery(q => new { Address = q.Select(x => x.ShippingId), Count = q.Count() });
                string before = grouped.Render(flavor).Text;
                string after = grouped.Page(2, 3).Render(flavor).Text;
                string Group(string sql) => Regex.Match(sql, @"GROUP BY (.*?)(?:ORDER BY|LIMIT|OFFSET|FOR JSON|$)").Groups[1].Value.Trim();
                Check(Group(before) == Group(after), "Paging changed grouping");
                Check(!after.Contains(tableId()), "Paging added the primary key");
                var distinct = new SelectQuery<Order>().Select(x => x.ShippingId).Distinct().Page(1, 3).Render(flavor).Text;
                Check(!distinct.Contains(tableId()), "Distinct paging added the primary key");
                string tableId() => Table.GetCached<Order>().PrimaryKey.Render(flavor);
                var aggregate = new SelectQueryInitializer<Order>().BuildQuery(q => new { Count = q.Count() }).Page(1, 1).Render(flavor);
                Check(!aggregate.Text.Contains("GROUP BY"), "Paging grouped an ungrouped aggregate");
            });
            test($"count preserves result shape and binds each parameter once for {flavor}", () =>
            {
                var query = new SelectQuery<Order>().Select(x => x.ShippingId).Distinct()
                    .Where(x => x.Id, Enumerable.Range(1, 1050).ToArray(), SqlArrayOp.IN).Page(2, 1);
                string before = query.Render(flavor).Text;
                var parameters = new ParameterCollection();
                string count = query.RenderForCount(parameters, flavor);
                Check(count.Contains("SELECT DISTINCT") && !count.Contains("ORDER BY") && !count.Contains("OFFSET"), "Count lost DISTINCT or retained paging");
                Check(parameters.Count == 1050, "Count supplied unused or duplicate predicate parameters");
                Check(Regex.Matches(count, @"[@$]P\d+\b").Select(x => x.Value).Distinct().Count() == parameters.Count, "Bound parameters differ from referenced parameters");
                Check(query.Render(flavor).Text == before, "Count changed the original query");
                var grouped = new SelectQueryInitializer<Order>().BuildQuery(q => new { Address = q.Select(x => x.ShippingId), Count = q.Count() });
                Check(grouped.RenderForCount(new ParameterCollection(), flavor).Contains("GROUP BY"), "Count removed grouping");
            });
            foreach (SqlOp op in new[] { SqlOp.EQ, SqlOp.NOT })
                test($"relationship filters render deterministically for {flavor}, {op}", () =>
                {
                    var query = new SelectQuery<SchemaRoot>().Select(x => x.Id)
                        .Where(x => x.Children.First().Name, "child", op)
                        .Where(x => x.Children.First().Id, 7, SqlOp.GT);
                    var first = query.Render(flavor);
                    for (int i = 0; i < 5; i++)
                    {
                        var next = query.Render(flavor);
                        Check(next.Text == first.Text, "Identical query changed SQL text");
                        Check(next.Parameters.GetAll().Select(x => x.Value).SequenceEqual(first.Parameters.GetAll().Select(x => x.Value)), "Rendering changed bindings");
                    }
                    var count = query.RenderForCount(new ParameterCollection(), flavor);
                    Check(count == query.RenderForCount(new ParameterCollection(), flavor), "Identical count changed SQL text");
                    Check(query.Render(flavor).Text == first.Text, "Count changed a relationship filter");
                });
            test($"large mixed filters retain their parameters for {flavor}", () =>
            {
                var query = new SelectQuery<Order>().Select(x => x.Id);
                for (int i = 0; i < 1000; i++) query.Where(x => x.Id, i, SqlOp.GT);
                query.Where(query.NewWhere(SqlWhereOp.OR).Where(x => x.ShippingId, 8, SqlOp.EQ).Where(x => x.BillingId, 9, SqlOp.EQ));
                var rendered = query.Render(flavor);
                Check(rendered.Parameters.Count == 1002 && rendered.Text.Contains(" OR "), "Simplification lost predicates or grouping");
            });
            foreach (bool output in new[] { false, true })
                foreach (bool caller in new[] { false, true })
                    foreach (bool fail in new[] { false, true })
                        test($"insert batches respect budgets and ownership for {flavor}, output={output}, caller={caller}, fail={fail}", () =>
                        {
                            var db = new FakeDatabase { FailOnExecution = fail ? 2 : 0 };
                            var connector = new FakeConnector(db, flavor);
                            connector.Options.ParameterLimit = 2;
                            using var connection = caller ? new FakeConnection(db) : null;
                            connection?.Open();
                            using var transaction = caller ? (FakeTransaction)connection.BeginTransaction() : null;
                            var insert = new InsertQuery<BatchRow>().InsertAuto(new[] { new BatchRow { Name = "one" }, new BatchRow { Name = "two" }, new BatchRow { Name = "three" } });
                            string before = insert.Render(flavor).Text;
                            ISqlResponse response;
                            if (output && flavor == DbFlavor.MySql)
                            {
                                Throws<NotSupportedException>(() => insert.Output(x => x.Id).ExecuteWithOutput(connector, transaction));
                                Check(db.Executed.Count == 0, "Unsupported output executed");
                                return;
                            }
                            if (output)
                            {
                                var rows = insert.Output(x => x.Id).ExecuteWithOutput(connector, transaction);
                                response = rows;
                                if (rows.State == ResponseState.Valid)
                                {
                                    using var table = rows.Result;
                                    Check(table.Rows.Count == 3, "Output batches were not combined");
                                }
                            }
                            else response = insert.Execute(connector, transaction);
                            Check(response.State == (fail ? ResponseState.Error : ResponseState.Valid), "Unexpected batch outcome");
                            Check(db.Connections.Count == 1 && db.Executed.Count == (fail ? 2 : 3), "Batches changed connections or row boundaries");
                            Check(db.Executed.All(x => x.Parameters.Count == 2 && x.IsDisposed), "Budget or command disposal violated");
                            var owned = db.Connections.Single();
                            var tx = owned.Transactions.Single();
                            Check(db.Executed.All(x => x.UsedTransaction == tx), "Batches used different transactions");
                            if (caller)
                                Check(!owned.IsDisposed && !tx.IsDisposed && tx.Commits == 0 && tx.Rollbacks == 0, "Completed caller resources");
                            else
                                Check(owned.IsDisposed && tx.IsDisposed && tx.Commits == (fail ? 0 : 1) && tx.Rollbacks == (fail ? 1 : 0), "Owned batch was not atomic or leaked resources");
                            if (!output) Check(insert.Render(flavor).Text == before, "Batching changed the original rows");
                        });
            test($"an oversized insert row returns an error before connecting for {flavor}", () =>
            {
                var db = new FakeDatabase();
                var connector = new FakeConnector(db, flavor); connector.Options.ParameterLimit = 1;
                var result = new InsertQuery<BatchRow>().InsertAuto(new BatchRow()).Execute(connector);
                Check(result.State == ResponseState.Error && db.Connections.Count == 0, "Oversized row touched the database or escaped error values");
            });
        }
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
        {
            test($"SQL Server VALUES row budget applies for {flavor}", () =>
            {
                var db = new FakeDatabase(); var connector = new FakeConnector(db, flavor);
                connector.Options.ParameterLimit = 10000;
                var result = new InsertQuery<BatchRow>().InsertAuto(Enumerable.Range(0, 1001).Select(i => new BatchRow { Id = i })).Execute(connector);
                Check(result.State == ResponseState.Valid && db.Executed.Select(x => x.Parameters.Count).SequenceEqual(new[] { 2000, 2 }), "SQL Server VALUES exceeded 1000 rows");
            });
            test($"SQL Server parameter ceiling cannot be raised for {flavor}", () =>
            {
                var db = new FakeDatabase(); var connector = new FakeConnector(db, flavor);
                connector.Options.ParameterLimit = 10000;
                var result = new InsertQuery<Item>().InsertAuto(Enumerable.Range(0, 1001).Select(i => new Item { Name = i.ToString() })).Execute(connector);
                Check(result.State == ResponseState.Valid && db.Executed.All(x => x.Parameters.Count <= 2100), "Configured budget exceeded the native parameter ceiling");
            });
        }
        test("primary keys are collected across insert batches", () =>
        {
            var db = new FakeDatabase(); var connector = new FakeConnector(db);
            connector.Options.ParameterLimit = 2;
            var result = new InsertWithPrimaryKeyQuery<BatchRow, int>().InsertAuto(new[] { new BatchRow(), new BatchRow(), new BatchRow() }).Execute(connector);
            Check(result.State == ResponseState.Valid && result.Result == 3 && result.TouchedPrimaryKeys.Count == 3, "Batch keys or count lost");
        });
        test("PostgreSQL DISTINCT JSON and totals use comparable projections", () =>
        {
            var query = new SelectQuery<Order>().Select(x => x.Billing.Name).Select(x => x.Shipping.Name).Distinct().Page(3, 2);
            string data = query.Render(DbFlavor.PostgreSql).Text;
            string count = query.RenderForCount(new ParameterCollection(), DbFlavor.PostgreSql);
            Check(new[] { data, count }.All(sql => sql.Contains("SELECT DISTINCT") && sql.Contains(" AS jsonb)")),
                "DISTINCT compared PostgreSQL relationship json values instead of jsonb");
            Check(!Regex.IsMatch(count, @"LIMIT 2\b|OFFSET 4\b") && count.Contains("LIMIT 1"), "Total changed root or relationship paging");
        });
        foreach (string stage in new[] { "open", "command", "parameter", "begin", "commit", "dispose-connection", "dispose-transaction" })
            test($"owned batch failures remain error values at {stage}", () =>
            {
                var db = new FakeDatabase { FailAt = stage };
                var connector = new FakeConnector(db); connector.Options.ParameterLimit = 2;
                var result = new InsertQuery<BatchRow>().InsertAuto(new[] { new BatchRow(), new BatchRow() }).Execute(connector);
                Check(result.State == ResponseState.Error && result.Error.ToString().Contains(stage), "Batch failure escaped or lost its cause");
                Check(db.Connections.Single().IsDisposed && db.Connections.Single().Commands.All(x => x.IsDisposed), "Failed batch leaked owned resources");
            });
        test("batch rollback failure preserves the original execution error", () =>
        {
            var db = new FakeDatabase { FailOnExecution = 2, FailRollback = true };
            var connector = new FakeConnector(db); connector.Options.ParameterLimit = 2;
            var result = new InsertWithPrimaryKeyQuery<BatchRow, int>().InsertAuto(new[] { new BatchRow(), new BatchRow() }).Execute(connector);
            Check(result.State == ResponseState.Error && result.Error.ToString().Contains("execution 2") && result.TouchedPrimaryKeys == null, "Rollback replaced the execution error or exposed partial keys");
        });
        test("metadata caches distinguish identical names from different assemblies", () =>
        {
            Type Model(string assembly, string field)
            {
                var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assembly), AssemblyBuilderAccess.Run)
                    .DefineDynamicModule(assembly).DefineType("Same.Model", TypeAttributes.Public);
                builder.DefineField(field, typeof(int), FieldAttributes.Public);
                return builder.CreateType();
            }
            var a = Model("ModelA", "Left"); var b = Model("ModelB", "Right");
            Check(Table.GetCached(a).SourceType == a && Table.GetCached(b).SourceType == b, "Table cache confused runtime types");
            Check(Table.GetCached(b).Columns.TryGet("Right") != null && Table.GetCached(b).Columns.TryGet("Left") == null, "Parsed metadata cache confused runtime types");
        });
        test("column identity still follows public metadata mutations", () =>
        {
            var left = new Table(null, "left"); var right = new Table(null, "right");
            var column = new Column(left, "Id");
            var first = column.UUID;
            column.ColumnName = "Name";
            var renamed = column.UUID;
            column.Table = right;
            Check(first != renamed && column.UUID != renamed && column.UUID == new Column(right, "Name").UUID, "Cached identity ignored a public mutation");
        });
    }

    internal static void MeasureRendering()
    {
        foreach (int count in new[] { 10, 100, 1000 })
        {
            var query = new SelectQuery<Order>().Select(x => x.Id);
            for (int i = 0; i < count; i++) query.Where(x => x.Id, i, SqlOp.GT);
            for (int i = 0; i < 3; i++) query.Render(DbFlavor.PostgreSql);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) query.Render(DbFlavor.PostgreSql);
            Console.WriteLine($"RENDER_ALLOCATION predicates={count} bytes_per_render={(GC.GetAllocatedBytesForCurrentThread() - before) / 10}");
            before = GC.GetAllocatedBytesForCurrentThread();
            var snapshot = query.Capture();
            long captureBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) snapshot.Render(DbFlavor.PostgreSql);
            Console.WriteLine($"SNAPSHOT_ALLOCATION predicates={count} bytes_per_capture={captureBytes} bytes_per_render={(GC.GetAllocatedBytesForCurrentThread() - before) / 10}");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class FlatOrderQuery : SqlLinqer.Queries.Core.SelectQuery
    {
        internal FlatOrderQuery(Table table) : base(table)
        {
            SelectColumn(table.FindColumnFromPath(new[] { nameof(Order.Billing), nameof(Address.Name) }), "Billing", false);
            SelectColumn(table.FindColumnFromPath(new[] { nameof(Order.Shipping), nameof(Address.Name) }), "Shipping", false);
        }
    }
    [SqlTable("orders")]
    internal sealed class Order
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int BillingId { get; set; }
        public int ShippingId { get; set; }
        [SqlOneToOne(nameof(BillingId))] public Address Billing { get; set; }
        [SqlOneToOne(nameof(ShippingId))] public Address Shipping { get; set; }
    }
    [SqlTable("addresses")]
    internal sealed class Address
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
    }
    [SqlTable("roots", "root_schema")]
    internal sealed class SchemaRoot
    {
        [SqlPrimaryKey] public int Id { get; set; }
        [SqlOneToMany(nameof(Child.RootId))] public List<Child> Children { get; set; }
        [SqlOneToOne(nameof(Id))] public FixedChild Fixed { get; set; }
        [SqlManyToMany("root_tags", "RootId", "TagId")] public List<Address> Tags { get; set; }
        [SqlManyToMany("links", "root_tags", "RootId", "TagId")] public List<Address> FixedTags { get; set; }
        [SqlManyToMany(typeof(Link), nameof(Link.RootId), nameof(Link.TagId))] public List<Address> TypedTags { get; set; }
    }
    internal sealed class Child
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int RootId { get; set; }
        public string Name { get; set; }
    }
    [SqlTable("fixed", "shared")]
    internal sealed class FixedChild { [SqlPrimaryKey] public int Id { get; set; } }
    internal sealed class Link { public int RootId { get; set; } public int TagId { get; set; } }
    [SqlAutoInsert]
    internal sealed class BatchRow
    {
        [SqlPrimaryKey(false)] public int Id { get; set; }
        public string Name { get; set; }
    }
}
