#nullable enable annotations
using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.SqlServer;

internal static partial class CallableChecks
{
    private static string InputsProcedure(string schema) => $"""
        CREATE PROCEDURE [{schema}].[ProcedureInputs]
            @Name nvarchar(80) = N'database-default', @PlainName nvarchar(80) = N'plain-default',
            @Count int = 73, @Enabled bit = 1
        AS BEGIN SET NOCOUNT ON;
            SELECT @Name AS Name, @PlainName AS PlainName, @Count AS Count, @Enabled AS Enabled;
            RETURN 0;
        END
        """;

    private static string RequiredInputsProcedure(string schema) => $"""
        CREATE PROCEDURE [{schema}].[ProcedureRequiredInputs] @Name nvarchar(80)
        AS BEGIN SET NOCOUNT ON;
            SELECT @Name AS Name, N'plain-default' AS PlainName, 73 AS Count, CAST(1 AS bit) AS Enabled;
        END
        """;

    private static void RunModelLocal(Action<string, Action> local, Action<bool, string> require)
    {
        local("procedure class nullable inputs and explicit SQL NULL bind with SqlClient", () =>
        {
            LocalChecks.Parse(InputsProcedure("probe"));
            LocalChecks.Parse(RequiredInputsProcedure("probe"));
            var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"),
                DbFlavor.AzureSql, new ConnectorOptions { DefaultSchema = "probe" });
            var ordinary = new InputsCall().Render(new ProcedureInput(), connector);
            var explicitNull = new ExplicitInputsCall().Render(new ExplicitProcedureInput
            {
                Name = new SqlArgument<string?>(null),
                PlainName = new SqlArgument<string>(null),
                Count = new SqlArgument<int?>(null),
                Enabled = new SqlArgument<bool?>(null)
            }, connector);
            foreach (var query in new[] { ordinary, explicitNull })
            {
                LocalChecks.Parse(query.Text);
                LocalChecks.Parse(query.GetTextWithParameterValues());
                using var command = new SqlCommand();
                typeof(BaseConnector).GetMethod("ImportFromTempQueryData", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(connector, new object[] { command, query });
                var inputs = command.Parameters.Cast<Microsoft.Data.SqlClient.SqlParameter>()
                    .Where(p => p.Direction == ParameterDirection.Input).ToArray();
                if (ReferenceEquals(query, ordinary)) require(inputs.Length == 0, "Ordinary null inputs were sent");
                else require(inputs.Length == 4 && inputs.All(p => p.Value == DBNull.Value)
                    && inputs.Count(p => p.SqlDbType == SqlDbType.NVarChar && p.Size == 80) == 2
                    && inputs.Count(p => p.SqlDbType == SqlDbType.Int) == 1 && inputs.Count(p => p.SqlDbType == SqlDbType.Bit) == 1,
                    "Explicit NULL bindings lost CLR type metadata");
            }
        });
    }

    private static void RunModelLive(BaseConnector connector, SqlConnection connection, Action<string, Action> live)
    {
        live("procedure classes apply database defaults and preserve explicit NULL empty and supplied inputs", () =>
        {
            var schema = connector.Options.DefaultSchema;
            try
            {
                using (var create = new SqlCommand(InputsProcedure(schema), connection)) create.ExecuteNonQuery();
                using (var create = new SqlCommand(RequiredInputsProcedure(schema), connection)) create.ExecuteNonQuery();
                foreach (bool asynchronous in new[] { false, true })
                {
                    var ordinary = new InputsCall();
                    foreach (string? name in new[] { null, string.Empty, "  ", "雪" })
                    {
                        var input = new ProcedureInput { Name = name, PlainName = name!, Count = name == null ? null : 0, Enabled = name == null ? null : false };
                        var response = asynchronous ? ordinary.ExecuteAsync(input, connector).GetAwaiter().GetResult() : ordinary.Execute(input, connector);
                        var row = LiveSuite.Valid(response).Single();
                        LiveSuite.Require(row.Name == (name ?? "database-default") && row.PlainName == (name ?? "plain-default")
                            && row.Count == (name == null ? 73 : 0) && row.Enabled == (name == null) && response.ReturnCode == 0
                            && response.TotalResults == null,
                            "Ordinary nullable input did not preserve database defaults or supplied values");
                    }
                    var explicitCall = new ExplicitInputsCall();
                    var explicitInputs = new[]
                    {
                        new ExplicitProcedureInput(),
                        new ExplicitProcedureInput { Name = new SqlArgument<string?>(null), PlainName = new SqlArgument<string>(null),
                            Count = new SqlArgument<int?>(null), Enabled = new SqlArgument<bool?>(null) },
                        new ExplicitProcedureInput { Name = string.Empty, PlainName = string.Empty, Count = 0, Enabled = false },
                        new ExplicitProcedureInput { Name = "雪", PlainName = "雪", Count = 7, Enabled = true }
                    };
                    for (int i = 0; i < explicitInputs.Length; i++)
                    {
                        var response = asynchronous ? explicitCall.ExecuteAsync(explicitInputs[i], connector).GetAwaiter().GetResult()
                            : explicitCall.Execute(explicitInputs[i], connector);
                        var row = LiveSuite.Valid(response).Single();
                        string? expected = i == 0 ? "database-default" : i == 1 ? null : i == 2 ? string.Empty : "雪";
                        LiveSuite.Require(row.Name == expected && row.PlainName == (i == 0 ? "plain-default" : expected)
                            && row.Count == (i == 0 ? 73 : i == 1 ? null : i == 2 ? 0 : (int?)7)
                            && row.Enabled == (i == 0 ? true : i == 1 ? null : i == 2 ? false : (bool?)true),
                            "Explicit argument omission, SQL NULL, or supplied values changed");
                    }
                    var required = new RequiredInputsCall();
                    var failure = asynchronous ? required.ExecuteAsync(new RequiredInput(), connector).GetAwaiter().GetResult()
                        : required.Execute(new RequiredInput(), connector);
                    LiveSuite.Require(failure.State == ResponseState.Error && failure.Result == null,
                        "Omitted required parameter was replaced with a library value");
                }
            }
            finally
            {
                using var drop = new SqlCommand($"DROP PROCEDURE IF EXISTS [{schema}].[ProcedureInputs]; DROP PROCEDURE IF EXISTS [{schema}].[ProcedureRequiredInputs];", connection);
                drop.ExecuteNonQuery();
            }
        });
    }

    public class ProcedureInput
    {
        [SqlParameter(Size = 80)] public string? Name { get; set; }
        [SqlParameter(Size = 80)] public string PlainName { get; set; } = null!;
        public int? Count { get; set; }
        public bool? Enabled { get; set; }
    }
    public class ExplicitProcedureInput
    {
        [SqlParameter(Size = 80)] public SqlArgument<string?> Name { get; set; }
        [SqlParameter(Size = 80)] public SqlArgument<string> PlainName { get; set; }
        public SqlArgument<int?> Count { get; set; }
        public SqlArgument<bool?> Enabled { get; set; }
    }
    public class RequiredInput { [SqlParameter(Size = 80)] public string? Name { get; set; } }
    public class InputRow
    {
        public string? Name { get; set; }
        public string? PlainName { get; set; }
        public int? Count { get; set; }
        public bool? Enabled { get; set; }
    }
    [SqlProcedure("ProcedureInputs")]
    public sealed class InputsCall : SqlLinqerCallable<ProcedureInput, InputRow> { }
    [SqlProcedure("ProcedureInputs")]
    public sealed class ExplicitInputsCall : SqlLinqerCallable<ExplicitProcedureInput, InputRow> { }
    [SqlProcedure("ProcedureRequiredInputs")]
    public sealed class RequiredInputsCall : SqlLinqerCallable<RequiredInput, InputRow> { }
}
