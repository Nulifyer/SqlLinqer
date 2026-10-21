using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

// The native SQLite and DuckDB runners compile this same fixture.
internal static class CompatibleDatabaseTests
{
    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        void Sql(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        long Count(string table) => Result(connector.ExecuteScalar<long>(new RenderedQuery("SELECT COUNT(*) FROM " + table)));
        Sql("CREATE TABLE compat_addresses (Id INTEGER PRIMARY KEY, Name VARCHAR); " +
            "CREATE TABLE compat_orders (Id INTEGER PRIMARY KEY, BillingId INTEGER, ShippingId INTEGER); " +
            "CREATE TABLE compat_lines (Id INTEGER PRIMARY KEY, OrderId INTEGER, Name VARCHAR); " +
            "CREATE TABLE compat_batches (Id INTEGER PRIMARY KEY, Name VARCHAR NOT NULL); " +
            "INSERT INTO compat_addresses VALUES (1,'billing'),(2,'shipping'); " +
            "INSERT INTO compat_orders VALUES (1,1,2),(2,2,1),(3,1,2); " +
            "INSERT INTO compat_lines VALUES (1,1,'first'),(2,1,'second'),(3,2,'other');");

        test("compatible: two relationships to the same table return independent values", () =>
        {
            using var rows = Result(connector.ExecuteReaderDataTable(new FlatQuery().Render(connector)));
            Check(rows.Rows.Count == 3, "Join changed row count");
            var first = rows.Rows.Cast<DataRow>().Single(x => Convert.ToInt32(x["Id"]) == 1);
            Check((string)first["Billing"] == "billing" && (string)first["Shipping"] == "shipping", "Relationship occurrences collapsed");
            var nested = Result(new SelectQuery<Order>().SelectRootColumns().Select(x => x.Billing.Name).Select(x => x.Shipping.Name)
                .Where(x => x.Id, 1, SqlOp.EQ).Execute(connector)).Single();
            Check(nested.Billing.Name == "billing" && nested.Shipping.Name == "shipping", "Nested relationship values collapsed");
        });
        test("compatible: grouped, distinct and aggregate pages retain result meaning", () =>
        {
            var grouped = new SelectQueryInitializer<Order>().BuildQuery(q => new { Address = q.Select(x => x.ShippingId), Count = q.Count() });
            var all = Result(grouped.Execute(connector));
            var page = grouped.Page(1, 1).ExecuteWithTotalCount(connector);
            var row = Result(page).Single();
            Check(page.TotalResults == 2 && all.Single(x => x.Address == row.Address).Count == row.Count, "Paging changed grouping");
            var distinct = new SelectQuery<Order>().Select(x => x.ShippingId).Distinct().Page(1, 1).ExecuteWithTotalCount(connector);
            Check(Result(distinct).Count == 1 && distinct.TotalResults == 2, "Distinct total counted input rows");
            var aggregate = new SelectQueryInitializer<Order>().BuildQuery(q => new { Count = q.Count() }).Page(1, 1).ExecuteWithTotalCount(connector);
            Check(Result(aggregate).Single().Count == 3 && aggregate.TotalResults == 1, "Ungrouped aggregate became grouped");
        });
        test("compatible: related tables resolve in the configured default schema", () =>
        {
            using var connection = connector.CreateConnection(); connection.Open();
            using var transaction = connection.BeginTransaction();
            string schema = connector.DbFlavor == DbFlavor.Sqlite
                ? "ATTACH DATABASE ':memory:' AS compat_tenant; " : "CREATE SCHEMA compat_tenant; ";
            Result(connector.ExecuteNonQuery(new RenderedQuery(schema +
                "CREATE TABLE compat_tenant.compat_addresses (Id INTEGER PRIMARY KEY, Name VARCHAR); " +
                "CREATE TABLE compat_tenant.compat_orders (Id INTEGER PRIMARY KEY, BillingId INTEGER, ShippingId INTEGER); " +
                "INSERT INTO compat_tenant.compat_addresses VALUES (10,'tenant-billing'),(20,'tenant-shipping'); " +
                "INSERT INTO compat_tenant.compat_orders VALUES (1,10,20);"), transaction));
            var row = Result(new SelectQuery<Order>("compat_tenant").Select(x => x.Id).Select(x => x.Billing.Name)
                .Select(x => x.Shipping.Name).Execute(connector, transaction)).Single();
            Check(row.Billing.Name == "tenant-billing" && row.Shipping.Name == "tenant-shipping", "Related values came from another schema");
            transaction.Rollback();
        });
        foreach (SqlOp op in new[] { SqlOp.EQ, SqlOp.NOT })
            test($"compatible: repeated relationship filters retain values, {op}", () =>
            {
                var query = new SelectQuery<Order>().Select(x => x.Id).Where(x => x.Lines.First().Name, "first", op);
                string sql = query.Render(connector).Text;
                int[] expected = op == SqlOp.EQ ? new[] { 1 } : new[] { 2, 3 };
                for (int i = 0; i < 3; i++)
                {
                    Check(query.Render(connector).Text == sql, "Relationship query SQL changed");
                    var result = query.ExecuteWithTotalCount(connector);
                    Check(Result(result).Select(x => x.Id).Order().SequenceEqual(expected) && result.TotalResults == expected.Length, "Relationship predicate or total changed");
                }
            });
        foreach (bool asynchronous in new[] { false, true })
        test($"compatible: SQL loading ignores API attributes and preserves API serialization, async={asynchronous}", () =>
        {
            var query = new SelectQuery<ApiAddress>().SelectRootColumns().Where(x => x.Id, 1, SqlOp.EQ);
            var model = Result(asynchronous ? query.ExecuteAsync(connector).GetAwaiter().GetResult() : query.Execute(connector)).Single();
            Check(model.Id == 1 && model.Name == "billing", "SQL-isolated loading lost mapped values");
            using var api = JsonDocument.Parse(JsonSerializer.Serialize(model));
            Check(!api.RootElement.TryGetProperty("Id", out _) && api.RootElement.GetProperty("displayName").GetString() == "billing", "API behavior changed");
        });
        BaseConnector Budget() => new ConnectionFactoryConnector(connector.CreateConnection, connector.DbFlavor,
            new ConnectorOptions { ParameterLimit = 2 });
        foreach (int count in new[] { 1, 2, 3, 5, 33 })
            test($"compatible: exact and partial batch boundaries preserve keys, rows={count}", () =>
            {
                var bounded = new ConnectionFactoryConnector(connector.CreateConnection, connector.DbFlavor,
                    new ConnectorOptions { ParameterLimit = 4 });
                var data = Enumerable.Range(1, count).Select(id => new BatchRow { Id = id, Name = $"row-{id}" }).ToArray();
                var query = new InsertWithPrimaryKeyQuery<BatchRow, int>().InsertAuto(data);
                string before = query.Render(connector).Text;
                try
                {
                    var response = query.Execute(bounded);
                    Check(Result(response) == count && response.TouchedPrimaryKeys.Order().SequenceEqual(Enumerable.Range(1, count)),
                        "Boundary batch changed affected count or keys");
                    Check(Count("compat_batches") == count && query.Render(connector).Text == before, "Batching mutated insert or lost rows");
                    var duplicate = query.Execute(bounded);
                    Check(duplicate.State == ResponseState.Error && duplicate.TouchedPrimaryKeys?.Any() != true, "Reused insert exposed stale successful keys");
                    Check(Count("compat_batches") == count && query.Render(connector).Text == before, "Failed reuse changed data or insert state");
                }
                finally { Sql("DELETE FROM compat_batches"); }
            });
        test("compatible: batch insert returns every primary key", () =>
        {
            var response = new InsertWithPrimaryKeyQuery<BatchRow, int>().InsertAuto(new[] {
                new BatchRow { Id = 1, Name = "one" }, new BatchRow { Id = 2, Name = "two" }, new BatchRow { Id = 3, Name = "three" }
            }).Execute(Budget());
            Check(Result(response) == 3 && response.TouchedPrimaryKeys.SequenceEqual(new[] { 1, 2, 3 }) && Count("compat_batches") == 3, "Batch keys or writes missing");
            Sql("DELETE FROM compat_batches");
        });
        test("compatible: batch output preserves rows", () =>
        {
            using var rows = Result(new InsertQuery<BatchRow>().InsertAuto(new[] {
                new BatchRow { Id = 1, Name = "one" }, new BatchRow { Id = 2, Name = "two" }
            }).Output(x => x.Id).Output(x => x.Name).ExecuteWithOutput(Budget()));
            Check(rows.Rows.Count == 2 && rows.Rows.Cast<DataRow>().Select(x => (string)x["Name"]).SequenceEqual(new[] { "one", "two" }), "Batch output lost rows or columns");
            Sql("DELETE FROM compat_batches");
        });
        foreach (bool output in new[] { false, true })
            test($"compatible: later batch failure rolls back owned transaction, output={output}", () =>
            {
                var insert = new InsertQuery<BatchRow>().InsertAuto(new[] {
                    new BatchRow { Id = 1, Name = "one" }, new BatchRow { Id = 1, Name = "duplicate" }
                });
                ISqlResponse response = output ? insert.Output(x => x.Id).ExecuteWithOutput(Budget()) : insert.Execute(Budget());
                Check(response.State == ResponseState.Error && Count("compat_batches") == 0, "Earlier batch committed after a later failure");
            });
        test("compatible: caller can roll back a successful batch", () =>
        {
            using var connection = connector.CreateConnection(); connection.Open();
            using var transaction = connection.BeginTransaction();
            var response = new InsertQuery<BatchRow>().InsertAuto(new[] {
                new BatchRow { Id = 1, Name = "one" }, new BatchRow { Id = 2, Name = "two" }
            }).Execute(Budget(), transaction);
            Check(Result(response) == 2 && transaction.Connection == connection && connection.State == ConnectionState.Open, "Batch completed caller resources");
            transaction.Rollback();
            Check(Count("compat_batches") == 0, "Caller rollback did not undo all batches");
        });
        test("compatible: empty and oversized inserts leave the database unchanged", () =>
        {
            Check(Result(new InsertQuery<BatchRow>().Execute(Budget())) == 0, "Empty insert failed");
            var tiny = new ConnectionFactoryConnector(connector.CreateConnection, connector.DbFlavor, new ConnectorOptions { ParameterLimit = 1 });
            var result = new InsertQuery<BatchRow>().InsertAuto(new BatchRow { Id = 1, Name = "wide" }).Execute(tiny);
            Check(result.State == ResponseState.Error && Count("compat_batches") == 0, "Oversized row executed");
        });
        Sql("DROP TABLE compat_lines; DROP TABLE compat_orders; DROP TABLE compat_addresses; DROP TABLE compat_batches;");
    }

    private static T Result<T>(SqlResponse<T> response) => response.State == ResponseState.Valid ? response.Result : throw response.Error;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class FlatQuery : SqlLinqer.Queries.Core.SelectQuery
    {
        internal FlatQuery() : base(Table.GetCached<Order>())
        {
            SelectColumn(Table.PrimaryKey);
            SelectColumn(Table.FindColumnFromPath(new[] { nameof(Order.Billing), nameof(Address.Name) }), "Billing", false);
            SelectColumn(Table.FindColumnFromPath(new[] { nameof(Order.Shipping), nameof(Address.Name) }), "Shipping", false);
        }
        public override string Render(ParameterCollection parameters, DbFlavor flavor) => Render(parameters, flavor, false);
    }
    [SqlTable("compat_orders")]
    private sealed class Order
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int BillingId { get; set; }
        public int ShippingId { get; set; }
        [SqlOneToOne(nameof(BillingId))] public Address Billing { get; set; }
        [SqlOneToOne(nameof(ShippingId))] public Address Shipping { get; set; }
        [SqlOneToMany(nameof(Line.OrderId))] public List<Line> Lines { get; set; }
    }
    [SqlTable("compat_addresses")]
    private sealed class Address
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
    }
    [SqlTable("compat_addresses")]
    private sealed class ApiAddress
    {
        [SqlPrimaryKey, JsonIgnore] public int Id { get; set; }
        [JsonPropertyName("displayName")] public string Name { get; set; }
    }
    [SqlTable("compat_lines")]
    private sealed class Line
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int OrderId { get; set; }
        public string Name { get; set; }
    }
    [SqlTable("compat_batches"), SqlAutoInsert]
    private sealed class BatchRow
    {
        [SqlPrimaryKey(false)] public int Id { get; set; }
        public string Name { get; set; }
    }
}
