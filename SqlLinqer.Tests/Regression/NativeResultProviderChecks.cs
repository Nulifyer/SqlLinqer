using System.Data;
using System.Text.Json;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Typed;
using Root = SqlLinqer.Tests.Regression.NativeResultRegressionTests.Root;

namespace SqlLinqer.Tests.Regression;

internal static class NativeResultProviderChecks
{
    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        test("native root-driven graph: strict transport, paging, complete roots and early cancellation", () => RunAsync(connector).GetAwaiter().GetResult());
        MappingOwnershipChecks.RunProvider(connector, test);
        DatabaseEqualityChecks.Run(connector, test);
        SqlServerNativeShapeChecks.Run(connector, test);
    }
    private static async Task RunAsync(BaseConnector source)
    {
        var flavor = source.DbFlavor; string schema = source.Options.DefaultSchema;
        string Q(string value) => value.DbWrap(flavor);
        string T(string value) => string.IsNullOrEmpty(schema) ? Q(value) : Q(schema) + "." + Q(value);
        var native = new ConnectionFactoryConnector(source.CreateConnection, flavor, new()
        { DefaultSchema = schema, CommandTimeout = source.Options.CommandTimeout, CancelCommand = source.Options.CancelCommand });
        using var c = source.CreateConnection(); await c.OpenAsync(); var created = new List<string>();
        async Task Sql(string sql)
        { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 120; await cmd.ExecuteNonQueryAsync(); }
        string text = flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "nvarchar(100)" : "varchar(100)";
        try
        {
            foreach (var item in new[]{
                ("native_roots",$"{Q("Id")} int,{Q("Name")} {text},{Q("ProfileId")} int"),
                ("native_profiles",$"{Q("Id")} int,{Q("Note")} {text}"),
                ("native_orders",$"{Q("Id")} int,{Q("ParentId")} int,{Q("Title")} {text}"),
                ("native_items",$"{Q("Id")} int,{Q("ParentId")} int,{Q("Label")} {text}"),
                ("native_tags",$"{Q("Id")} int,{Q("ParentId")} int")})
            { await Sql($"CREATE TABLE {T(item.Item1)} ({item.Item2})"); created.Add(item.Item1); }
            await Sql($"INSERT INTO {T("native_roots")} VALUES(7,'one',1),(7,'duplicate',1),(8,NULL,NULL)");
            await Sql($"INSERT INTO {T("native_profiles")} VALUES(1,NULL)");
            await Sql($"INSERT INTO {T("native_orders")} VALUES(9,7,'one'),(10,7,'two')");
            await Sql($"INSERT INTO {T("native_items")} VALUES(11,9,'first'),(12,10,NULL)");
            await Sql($"INSERT INTO {T("native_tags")} VALUES(5,7),(5,7)");
            for (int page = 0; page < 3; page++)
            {
                var query = new SelectQuery<Root>(schema).SelectRootColumns().Select(x => x.Profile).Select(x => x.Orders)
                    .Select(x => x.Orders.First().Items).Select(x => x.Tags).OrderByAuto().OrderBy(x => x.Name, SqlDir.ASC);
                if (page == 1) query.Limit(1, 1);
                if (page == 2)
                {
                    var orders = query.Selects.GetAll<SelectRelationship>().Single(r => r.Alias == nameof(Root.Orders));
                    orders.SubQuery.PagingControls.SetLimit(1, 1);
                }
                string before = query.Render(flavor).Text;
                var planned = query.Capture().RenderForExecution<Root>(native);
                Check(planned.NativePlan?.IsGraph == true, "Strict native provider check did not compile native graph");
                Check(!planned.Text.Contains("FOR JSON", StringComparison.OrdinalIgnoreCase) && !planned.Text.Contains("JSON_", StringComparison.OrdinalIgnoreCase), "Native execution still contains JSON");
                Check(query.Render(flavor).Text == before, "Native compilation changed public rendering");
                using var tx = c.BeginTransaction();
                var actual = Valid(query.Execute(native, tx));
                var asyncRows = Valid(await query.ExecuteAsync(native, tx));
                Check(Same(actual, asyncRows), "Native async graph differs, page=" + page);
                var streamed = new List<Root>(); await foreach (var r in query.ExecuteAsyncStream(native, tx)) streamed.Add(r);
                Check(Same(actual, streamed), "Native stream graph differs, page=" + page);
                if (page == 0) Check(actual.Count == 3 && actual.Count(r => r.Id == 7) == 2 && actual.Where(r => r.Id == 7).All(r => r.Tags.Count == 2), "Duplicate occurrences changed");
                tx.Rollback();
            }
            using (var tx = c.BeginTransaction())
            {
                var query = new SelectQuery<Root>(schema).SelectRootColumns().Select(x => x.Orders).Select(x => x.Orders.First().Items).OrderByAuto();
                await foreach (var r in query.ExecuteAsyncStream(native, tx))
                { Check(r.Id == 7 && r.Orders.Count == 2 && r.Orders.All(o => o.Items.Count == 1), "Partial root escaped before early stop"); break; }
                Check(tx.Connection == c && c.State == ConnectionState.Open, "Early stop changed caller ownership");
                if (flavor is DbFlavor.SqlServer or DbFlavor.AzureSql)
                {
                    using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT XACT_STATE()";
                    Check(Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1, "SQL Server caller transaction is not committable after early stop");
                    tx.Commit();
                }
                else tx.Rollback();
            }
        }
        finally
        {
            foreach (string table in created.AsEnumerable().Reverse()) await Sql($"DROP TABLE {T(table)}");
        }
    }
    private static bool Same(List<Root> a, List<Root> b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
    private static List<T> Valid<T>(SqlResponse<List<T>> r) { if (r.State != ResponseState.Valid) throw r.Error; return r.Result; }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
