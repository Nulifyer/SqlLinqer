using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.SqlServer;

internal static partial class CallableChecks
{
    internal static void RunLocal(Action<string, Action> local, Action<bool, string> require)
    {
        RunModelLocal(local, require);
        RunNoInputLocal(local, require);
        RunMultiLocal(local, require);
        RunTypedTvpLocal(local, require);
        local("procedure standard metadata syntax and real SqlClient binding", () =>
        {
            var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"),
                DbFlavor.SqlServer, new ConnectorOptions { DefaultSchema = "probe" });
            var query = new PageCall().Render(new PageInput(), connector);
            using var command = new SqlCommand();
            typeof(BaseConnector).GetMethod("ImportFromTempQueryData", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(connector, new object[] { command, query });
            LocalChecks.Parse(command.CommandText);
            LocalChecks.Parse(query.GetTextWithParameterValues());
            var outputs = command.Parameters.Cast<Microsoft.Data.SqlClient.SqlParameter>().Where(p => p.Direction == ParameterDirection.Output).ToArray();
            require(outputs.Length == 2 && outputs.Count(p => p.SqlDbType == SqlDbType.BigInt) == 1
                && outputs.Count(p => p.SqlDbType == SqlDbType.Int) == 1, "Standard total or RETURN metadata differs");
            LocalChecks.Parse(PageProcedure("probe"));
        });
    }
    internal static void RunLive(BaseConnector connector, SqlConnection connection, Action<string, Action> live)
    {
        RunModelLive(connector, connection, live);
        RunPageLive(connector, connection, live);
        RunNoInputLive(connector, connection, live);
        RunMultiLive(connector, connection, live);
        RunTypedTvpLive(connector, connection, live);
    }

    [SqlProcedure("ProcedurePage")]
    public sealed class PageCall : SqlLinqerCallable<PageInput, ProcedureRow> { }

    public sealed class PageInput
    {
        public int MinId { get; set; }
        public bool SuppressCount { get; set; }
        public bool EmptyPage { get; set; }
    }

    private static string PageProcedure(string schema) => $"""
        CREATE PROCEDURE [{schema}].[ProcedurePage]
            @MinId int, @SuppressCount bit, @EmptyPage bit, @TotalResults bigint OUTPUT
        AS BEGIN
            IF @SuppressCount = 1 SET NOCOUNT ON ELSE SET NOCOUNT OFF;
            UPDATE [{schema}].[Rows] SET Name = Name WHERE Id >= @MinId;
            SET @TotalResults = (SELECT COUNT_BIG(*) FROM [{schema}].[Rows] WHERE Id >= @MinId);
            SELECT Id, Name FROM [{schema}].[Rows] WHERE Id >= @MinId AND @EmptyPage = 0 ORDER BY Id;
            RETURN 17;
        END
        """;

    private static void RunPageLive(BaseConnector connector, SqlConnection connection, Action<string, Action> live)
    {
        live("two-model procedure rows totals NOCOUNT and caller transactions sync and async", () =>
        {
            var schema = connector.Options.DefaultSchema;
            using (var create = new SqlCommand(PageProcedure(schema), connection)) create.ExecuteNonQuery();
            try
            {
                foreach (bool asynchronous in new[] { false, true })
                    foreach (bool suppressCount in new[] { false, true })
                        foreach (bool emptyPage in new[] { false, true })
                        {
                            var input = new PageInput { MinId = 0, SuppressCount = suppressCount, EmptyPage = emptyPage };
                            var procedure = new PageCall();
                            var response = asynchronous ? procedure.ExecuteAsync(input, connector).GetAwaiter().GetResult() : procedure.Execute(input, connector);
                            var rows = LiveSuite.Valid(response);
                            LiveSuite.Require(response.TotalResults > 0 && response.ReturnCode == 17
                                && rows.Count == (emptyPage ? 0 : response.TotalResults)
                                && response.RowsAffected == (suppressCount ? null : (int?)response.TotalResults),
                                $"Procedure page metadata differs: async={asynchronous}, NOCOUNT={suppressCount}, empty={emptyPage}");
                        }
                foreach (bool asynchronous in new[] { false, true })
                {
                    using var transaction = connection.BeginTransaction();
                    var procedure = new PageCall();
                    var input = new PageInput { MinId = int.MaxValue };
                    var response = asynchronous ? procedure.ExecuteAsync(input, connector, transaction).GetAwaiter().GetResult()
                        : procedure.Execute(input, connector, transaction);
                    LiveSuite.Require(LiveSuite.Valid(response).Count == 0 && response.TotalResults == 0 && response.RowsAffected == 0
                        && transaction.Connection == connection, "Zero totals or caller ownership differs");
                    transaction.Rollback();
                }
                // Switching the output spelling exercises fresh server metadata without caller configuration.
                using (var alter = new SqlCommand(PageProcedure(schema).Replace("CREATE PROCEDURE", "ALTER PROCEDURE")
                    .Replace("@TotalResults", "@total_count"), connection)) alter.ExecuteNonQuery();
                foreach (bool asynchronous in new[] { false, true })
                {
                    var input = new PageInput { EmptyPage = true };
                    var procedure = new PageCall();
                    var response = asynchronous ? procedure.ExecuteAsync(input, connector).GetAwaiter().GetResult() : procedure.Execute(input, connector);
                    LiveSuite.Require(LiveSuite.Valid(response).Count == 0 && response.TotalResults > 0 && response.ReturnCode == 17,
                        "The alternate standard output name was not discovered automatically");
                }
                using (var alter = new SqlCommand(PageProcedure(schema).Replace("CREATE PROCEDURE", "ALTER PROCEDURE")
                    .Replace("@TotalResults bigint", "@TotalResults decimal(18,2)"), connection)) alter.ExecuteNonQuery();
                foreach (bool asynchronous in new[] { false, true })
                {
                    var input = new PageInput();
                    var procedure = new PageCall();
                    var response = asynchronous ? procedure.ExecuteAsync(input, connector).GetAwaiter().GetResult() : procedure.Execute(input, connector);
                    LiveSuite.Require(response.State == ResponseState.Error && response.Result == null && response.TotalResults == null
                        && response.ReturnCode == null && response.RowsAffected == null, "A nonintegral standard total output was accepted");
                }
            }
            finally
            {
                using var drop = new SqlCommand($"DROP PROCEDURE [{schema}].[ProcedurePage]", connection);
                drop.ExecuteNonQuery();
            }
        });
    }

}
