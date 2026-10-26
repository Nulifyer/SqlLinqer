using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.SqlServer;

internal static partial class CallableChecks
{
    private static string NoInputProcedure(string schema, string total = null) => $"""
        CREATE PROCEDURE [{schema}].[ProcedureNoInputs] {(total == null ? "" : $"@{total} bigint OUTPUT")}
        AS BEGIN SET NOCOUNT ON;
            {(total == null ? "" : $"SET @{total} = 123;")}
            SELECT CAST(42 AS int) AS Id, N'no-input' AS Name;
            RETURN 19;
        END
        """;

    private static void RunNoInputLocal(Action<string, Action> local, Action<bool, string> require)
    {
        local("no-input procedure syntax and SqlClient bindings contain only response parameters", () =>
        {
            foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            {
                var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"),
                    flavor, new ConnectorOptions { DefaultSchema = "probe" });
                var query = new NoInputCall().Render(connector);
                LocalChecks.Parse(query.Text);
                LocalChecks.Parse(query.GetTextWithParameterValues());
                foreach (string total in new[] { null, "TotalResults", "total_count" }) LocalChecks.Parse(NoInputProcedure("probe", total));
                using var command = new SqlCommand();
                typeof(BaseConnector).GetMethod("ImportFromTempQueryData", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(connector, new object[] { command, query });
                require(command.Parameters.Count == 2 && command.Parameters.Cast<Microsoft.Data.SqlClient.SqlParameter>()
                    .All(p => p.Direction == ParameterDirection.Output), "No-input call bound procedure inputs");
            }
        });
    }

    private static void RunNoInputLive(BaseConnector connector, SqlConnection connection, Action<string, Action> live)
    {
        live("no-input procedures return rows standard totals and RETURN codes sync and async", () =>
        {
            var schema = connector.Options.DefaultSchema;
            var previous = Default.Connector;
            try
            {
                using (var create = new SqlCommand(NoInputProcedure(schema), connection)) create.ExecuteNonQuery();
                Default.Connector = connector;
                foreach (string total in new[] { null, "TotalResults", "total_count" })
                {
                    using (var alter = new SqlCommand(NoInputProcedure(schema, total).Replace("CREATE PROCEDURE", "ALTER PROCEDURE"), connection))
                        alter.ExecuteNonQuery();
                    foreach (bool asynchronous in new[] { false, true })
                        foreach (string route in new[] { "default", "retained", "explicit" })
                        {
                            var procedure = new NoInputCall(route == "retained" ? connector : null);
                            var response = route == "explicit"
                                ? asynchronous ? procedure.ExecuteAsync(connector).GetAwaiter().GetResult() : procedure.Execute(connector)
                                : asynchronous ? procedure.ExecuteAsync().GetAwaiter().GetResult() : procedure.Execute();
                            var row = LiveSuite.Valid(response).Single();
                            LiveSuite.Require(row.Id == 42 && row.Name == "no-input" && response.ReturnCode == 19
                                && response.TotalResults == (total == null ? null : (long?)123) && response.RowsAffected == null,
                                $"No-input result or metadata differs: {route}, async={asynchronous}, total={total}");
                        }
                }
                foreach (bool asynchronous in new[] { false, true })
                {
                    using var transaction = connection.BeginTransaction();
                    var procedure = new NoInputCall(connector);
                    var response = asynchronous ? procedure.ExecuteAsync(transaction).GetAwaiter().GetResult() : procedure.Execute(transaction);
                    LiveSuite.Require(LiveSuite.Valid(response).Single().Id == 42 && transaction.Connection == connection,
                        "No-input call changed caller transaction ownership");
                    transaction.Rollback();
                }
            }
            finally
            {
                Default.Connector = previous;
                using var drop = new SqlCommand($"DROP PROCEDURE IF EXISTS [{schema}].[ProcedureNoInputs]", connection);
                drop.ExecuteNonQuery();
            }
        });
    }

    [SqlProcedure("ProcedureNoInputs")]
    public sealed class NoInputCall(BaseConnector connector = null) : SqlLinqerCallable<ProcedureRow>(connector) { }
}
