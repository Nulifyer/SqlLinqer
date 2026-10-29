using Microsoft.Data.SqlClient;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.SqlServer;

internal static partial class CallableChecks
{
    private static string MultiProcedure(string schema) => $"""
        CREATE PROCEDURE [{schema}].[ProcedureMulti]
            @Mode int = 0, @EmptyMask int = 0, @SuppressCount bit = 1
        AS BEGIN
            IF @SuppressCount = 1 SET NOCOUNT ON ELSE SET NOCOUNT OFF;
            UPDATE [{schema}].[MultiEffects] SET Value = Value + 1;
            SELECT CAST(42 AS int) AS Id, N'multi' AS Name WHERE (@EmptyMask & 1) = 0;
            IF @Mode = 5 BEGIN; THROW 50000, 'later table failed', 1; END;
            IF @Mode = 6 BEGIN
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock @Resource = N'{schema}.ProcedureMulti.Waiting',
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
                IF @lockResult < 0 BEGIN; THROW 50000, 'Could not signal procedure execution', 1; END;
                WAITFOR DELAY '00:00:10';
            END;
            SELECT N'orders' AS Label WHERE (@EmptyMask & 2) = 0;
            IF @Mode = 1 RETURN 19;
            IF @Mode = 3 SELECT N'not-a-number' AS Value;
            ELSE SELECT CAST(123 AS bigint) AS Value WHERE (@EmptyMask & 4) = 0;
            IF @Mode = 2 SELECT 1 AS Extra;
            RETURN 19;
        END
        """;

    private static void RunMultiLocal(Action<string, Action> local, Action<bool, string> require)
    {
        local("multi procedure SQL parses with input and no-input SqlClient bindings", () =>
        {
            foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            {
                var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"), flavor,
                    new ConnectorOptions { DefaultSchema = "probe" });
                foreach (var query in new[] { new MultiNoInput().Render(connector), new MultiWithInput().Render(new MultiArgs(), connector) })
                {
                    LocalChecks.Parse(query.Text); LocalChecks.Parse(query.GetTextWithParameterValues());
                    using var command = new SqlCommand();
                    typeof(BaseConnector).GetMethod("ImportFromTempQueryData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(connector, new object[] { command, query });
                    require(command.Parameters.Cast<SqlParameter>().Count(p => p.Direction == System.Data.ParameterDirection.Output) == 1,
                        "Multi callable bound a global total or lost the RETURN parameter");
                }
                LocalChecks.Parse(MultiProcedure("probe"));
            }
        });
    }

    private static void RunMultiLive(BaseConnector connector, SqlConnection connection, Action<string, Action> live)
    {
        live("multi procedures map mixed collections empty tables metadata and default inputs sync and async", () =>
        {
            var schema = connector.Options.DefaultSchema;
            using (var create = new SqlCommand($"CREATE TABLE [{schema}].[MultiEffects] (Value int NOT NULL); INSERT INTO [{schema}].[MultiEffects] VALUES (0);", connection)) create.ExecuteNonQuery();
            try
            {
                using (var create = new SqlCommand(MultiProcedure(schema), connection)) create.ExecuteNonQuery();
                foreach (bool asynchronous in new[] { false, true })
                {
                    var procedure = new MultiNoInput(connector);
                    var noInput = asynchronous ? procedure.ExecuteAsync().GetAwaiter().GetResult() : procedure.Execute();
                    var (users, labels, totals) = noInput.GetResultOrThrow();
                    LiveSuite.Require(users.Single().Id == 42 && users[0].Name == "multi" && labels.Single() == "orders" && totals.Single() == 123 && noInput.ReturnCode == 19,
                        "No-input multi results or return code differ");
                    foreach (bool suppress in new[] { false, true }) foreach (int mask in Enumerable.Range(0, 8))
                    {
                        var input = new MultiArgs { EmptyMask = mask, SuppressCount = suppress };
                        var response = asynchronous ? new MultiWithInput().ExecuteAsync(input, connector).GetAwaiter().GetResult()
                            : new MultiWithInput().Execute(input, connector);
                        var result = response.GetResultOrThrow();
                        if ((mask & 1) == 0) LiveSuite.Require(result.Users.Single().Id == 42 && result.Users[0].Name == "multi", "First table values differ");
                        if ((mask & 2) == 0) LiveSuite.Require(result.Labels.Single() == "orders", "Second table values differ");
                        if ((mask & 4) == 0) LiveSuite.Require(result.Totals.Single() == 123L, "Third table values differ");
                        LiveSuite.Require(result.Users.Count == ((mask & 1) == 0 ? 1 : 0) && result.Labels.Length == ((mask & 2) == 0 ? 1 : 0)
                            && result.Totals.Count == ((mask & 4) == 0 ? 1 : 0) && response.ReturnCode == 19 && response.RowsAffected == (suppress ? null : (int?)1),
                            $"Multi results differ: async={asynchronous}, mask={mask}, NOCOUNT={suppress}");
                    }
                    foreach (int mode in new[] { 1, 2, 3, 5 })
                    {
                        int Before()
                        {
                            using var read = new SqlCommand($"SELECT Value FROM [{schema}].[MultiEffects]", connection);
                            return (int)read.ExecuteScalar();
                        }
                        int before = Before();
                        var input = new MultiArgs { Mode = mode };
                        var response = asynchronous ? new MultiWithInput().ExecuteAsync(input, connector).GetAwaiter().GetResult()
                            : new MultiWithInput().Execute(input, connector);
                        LiveSuite.Require(response.State == ResponseState.Error && response.Result == default && response.ReturnCode == null
                            && response.RowsAffected == null && Before() == before, "Later result failure leaked output or committed mutations");
                    }
                    foreach (int mode in new[] { 0, 3 })
                    {
                        using var transaction = connection.BeginTransaction();
                        var input = new MultiArgs { Mode = mode };
                        var retained = new MultiWithInput(connector);
                        var response = asynchronous ? retained.ExecuteAsync(input, transaction).GetAwaiter().GetResult() : retained.Execute(input, transaction);
                        LiveSuite.Require(response.State == (mode == 0 ? ResponseState.Valid : ResponseState.Error)
                            && transaction.Connection == connection, "Multi callable changed borrowed transaction ownership");
                        transaction.Rollback();
                    }
                }
                int ReadEffects()
                {
                    using var read = new SqlCommand($"SELECT Value FROM [{schema}].[MultiEffects]", connection);
                    return (int)read.ExecuteScalar();
                }
                bool CancellationBarrierHeld()
                {
                    using var observe = new SqlCommand("SELECT APPLOCK_TEST('public', @Resource, 'Exclusive', 'Session')", connection);
                    observe.Parameters.AddWithValue("@Resource", schema + ".ProcedureMulti.Waiting");
                    return Convert.ToInt32(observe.ExecuteScalar()) == 0;
                }
                int effectsBeforeCancellation = ReadEffects();
                using var cancellation = new CancellationTokenSource();
                var pending = new MultiWithInput().ExecuteAsync(new MultiArgs { Mode = 6 }, connector, cancellationToken: cancellation.Token);
                try
                {
                    // The procedure signals after UPDATE and its first SELECT, without requiring server-wide monitoring permissions.
                    bool waiting = false;
                    var deadline = System.Diagnostics.Stopwatch.StartNew();
                    while (!pending.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(8))
                    {
                        if (CancellationBarrierHeld()) { waiting = true; break; }
                        Task.Delay(25).GetAwaiter().GetResult();
                    }
                    LiveSuite.Require(waiting, "Did not observe the procedure waiting after its mutation and first table");
                    cancellation.Cancel();
                    try
                    {
                        pending.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                        throw new InvalidOperationException("Expected multi procedure cancellation");
                    }
                    catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }
                    LiveSuite.Require(!CancellationBarrierHeld(), "Cancellation retained the procedure transaction's application lock");
                    LiveSuite.Require(ReadEffects() == effectsBeforeCancellation, "Cancellation committed the earlier database mutation");
                    LiveSuite.Require(new MultiNoInput().Execute(connector).State == ResponseState.Valid, "Connector was not reusable after cancellation");
                }
                finally
                {
                    cancellation.Cancel();
                    try { pending.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
                }
            }
            finally
            {
                using var drop = new SqlCommand($"DROP PROCEDURE IF EXISTS [{schema}].[ProcedureMulti]; DROP TABLE [{schema}].[MultiEffects];", connection);
                drop.ExecuteNonQuery();
            }
        });
    }

    [SqlProcedure("ProcedureMulti")]
    public sealed class MultiNoInput(BaseConnector connector = null)
        : SqlLinqerMultiCallable<(List<ProcedureRow> Users, string[] Labels, IReadOnlyList<long> Totals)>(connector)
    { }
    [SqlProcedure("ProcedureMulti")]
    public sealed class MultiWithInput(BaseConnector connector = null)
        : SqlLinqerMultiCallable<MultiArgs, (List<ProcedureRow> Users, string[] Labels, IReadOnlyList<long> Totals)>(connector)
    { }
    public sealed class MultiArgs
    {
        public int Mode { get; set; }
        public int EmptyMask { get; set; }
        public bool SuppressCount { get; set; } = true;
    }
}
