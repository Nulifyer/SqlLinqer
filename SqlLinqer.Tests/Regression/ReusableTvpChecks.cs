using System.Data;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class ReusableTvpChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static FakeConnector Connector(FakeDatabase database, DbFlavor flavor)
    {
        database.Total = 1;
        return new(database, flavor);
    }

    internal static void Run(Action<string, Action> test)
    {
        test("named TVP rendering preserves the explicit schema and provider binding", () =>
        {
            var parameters = new SqlLinqer.Components.Render.ParameterCollection();
            var query = new InsertQuery<IdOnly>().UseTvpType("Id.Rows]", "app's schema").InsertAuto(new IdOnly { Id = 7 });
            var rendered = query.RenderTVP(parameters, DbFlavor.SqlServer, "other");
            using var payload = (DataTable)parameters.GetAll().Single().Value;
            Check(rendered.TempTypeId == "[app's schema].[Id.Rows]]]" && (int)payload.Rows[0]["Id"] == 7,
                "Public TVP rendering discarded the named type or failed to quote it");
        });
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql }) foreach (bool asynchronous in new[] { false, true })
        {
            foreach (bool alwaysMissing in new[] { false, true })
                test($"TVP pre-write guarding retries metadata only and bounds removal races, {flavor}, async={asynchronous}, missing={alwaysMissing}", () =>
                {
                    var db = new FakeDatabase(); var connector = Connector(db, flavor); int guards = 0;
                    db.ScalarFactory = () => db.Executed.Last().CommandText.Contains("@LockMode='Shared'")
                        && db.Executed.Last().CommandText.Contains("SELECT 0; RETURN; END; SELECT 1;")
                        ? (++guards == 1 || alwaysMissing ? 0 : 1) : 1;
                    var query = new InsertQuery<IdOnly>().InsertAuto(new IdOnly { Id = 7 });
                    var result = asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector);
                    Check(guards == (alwaysMissing ? 3 : 2), "Provisioning races were not bounded or rechecked");
                    Check(result.State == (alwaysMissing ? ResponseState.Error : ResponseState.Valid)
                        && db.Executed.Count(c => c.CommandText.StartsWith("INSERT INTO")) == (alwaysMissing ? 0 : 1),
                        "Metadata retries resent a write or sent rows without a guard");
                    Check(db.Connections.All(c => c.IsDisposed), "A retry leaked a connection");
                    foreach (var command in db.Executed.Where(c => c.CommandText.Contains("sp_getapplock")))
                        SqlLinqer.Tests.SqlServer.LocalChecks.Parse(command.CommandText);
                });
            test($"TVP lease configuration rejects invalid durations before writes, {flavor}, async={asynchronous}", () =>
            {
                foreach (var duration in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromDays(366) })
                {
                    var db = new FakeDatabase(); var connector = Connector(db, flavor); connector.Options.TvpLeaseDuration = duration;
                    var query = new InsertQuery<IdOnly>().InsertAuto(new IdOnly { Id = 7 });
                    var result = asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector);
                    Check(result.State == ResponseState.Error && !db.Executed.Any(c => c.CommandText.StartsWith("INSERT INTO")),
                        "Invalid duration reached the write or escaped as an exception");
                }
            });
            test($"default TVPs provision once reuse the SQL contract and retain payload ownership, {flavor}, async={asynchronous}", () =>
            {
                var database = new FakeDatabase(); var connector = Connector(database, flavor);
                Run(new InsertQuery<ReuseRow>().InsertAuto(new ReuseRow { Id = 1, Amount = 1.25m }));
                Run(new InsertQuery<ReuseRow>().InsertAuto(new ReuseRow { Id = 2, Amount = 9.75m }));
                var ensure = database.Executed.Where(c => c.CommandText.Contains("sys.dm_exec_describe_first_result_set")).ToArray();
                SqlLinqer.Tests.SqlServer.LocalChecks.Parse(ensure.Single().CommandText);
                var writes = database.Executed.Where(c => c.CommandText.StartsWith("INSERT INTO")).ToArray();
                Check(ensure.Length == 1 && writes.Length == 2 && !database.Executed.Any(c => c.CommandText.Contains("DROP TYPE")), "Warm reuse repeated type provisioning or removed a shared type");
                var names = writes.Select(c => c.Parameters.Cast<System.Data.Common.DbParameter>().Single().GetType().GetProperty("TypeName").GetValue(c.Parameters[0])).ToArray();
                Check(Equals(names[0], names[1]) && names[0].ToString().Contains("SqlLinqerTvp_v1_"), "The type name depends on row values rather than their SQL contract");
                Check(ensure[0].UsedTransaction != writes[0].UsedTransaction && database.Connections.All(c => c.IsDisposed), "Type provisioning borrowed the write transaction or leaked connections");
                if (asynchronous) Check(database.SyncCommands == 0, "Async reuse issued synchronous commands");
                void Run(InsertQuery<ReuseRow> query)
                {
                    var response = asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector);
                    Check(response.State == ResponseState.Valid && response.Result == 1, response.Error?.ToString());
                }
            });
            test($"reusable TVP SQL contracts distinguish decimal scales and column subsets, {flavor}, async={asynchronous}", () =>
            {
                var db = new FakeDatabase(); var connector = Connector(db, flavor);
                foreach (decimal amount in new[] { 1m, 1.25m })
                {
                    var query = new InsertQuery<ReuseRow>().InsertAuto(new ReuseRow { Id = 1, Amount = amount });
                    var response = asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector);
                    Check(response.State == ResponseState.Valid, response.Error?.ToString());
                }
                var other = new InsertQuery<IdOnly>().InsertAuto(new IdOnly { Id = 1 });
                Check((asynchronous ? other.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : other.ExecuteTvp(connector)).State == ResponseState.Valid, "Subset insert failed");
                var ensures = db.Executed.Where(c => c.CommandText.Contains("sys.dm_exec_describe_first_result_set")).ToArray();
                Check(ensures.Length == 3 && ensures[0].CommandText.Contains("DECIMAL(38,0)") && ensures[1].CommandText.Contains("DECIMAL(38,2)"), "Different SQL contracts were collapsed");
                Check(ensures.All(c => c.CommandText.Contains("sys.dm_exec_describe_first_result_set") && c.CommandText.Contains("SqlLinqer.TvpContract.v1")), "Provisioning skips catalog validation or ownership marking");
            });
            foreach (string stage in new[] { "dispose-command", "dispose-transaction", "dispose-connection" })
                test($"reusable TVP resource failures stay error values at {stage}, {flavor}, async={asynchronous}", () =>
                {
                    var db = new FakeDatabase { FailAt = stage }; var connector = Connector(db, flavor);
                    var query = new InsertQuery<IdOnly>().InsertAuto(new IdOnly { Id = 7 });
                    var response = asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector);
                    Check(response.State == ResponseState.Error && response.Result == 0 && db.Connections.All(c => c.IsDisposed),
                        "Resource cleanup escaped the response contract or leaked a connection");
                });
            test($"failed reusable provisioning never runs writes and remains retryable, {flavor}, async={asynchronous}", () =>
            {
                var db = new FakeDatabase { FailSql = "DECLARE @catalogLock" }; var connector = Connector(db, flavor);
                var query = new InsertQuery<IdOnly>().InsertAuto(new IdOnly { Id = 7 });
                var failed = asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector);
                Check(failed.State == ResponseState.Error && !db.Executed.Any(c => c.CommandText.StartsWith("INSERT INTO")), "Provisioning failure executed the write");
                db.FailSql = null;
                var success = asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector);
                Check(success.State == ResponseState.Valid && db.Executed.Count(c => c.CommandText.Contains("sys.dm_exec_describe_first_result_set")) == 2, "A failed ensure was cached as success");
            });
        }
    }

    private sealed class ReuseRow { public int Id { get; set; } public decimal Amount { get; set; } }
    private sealed class IdOnly { public int Id { get; set; } }
}
