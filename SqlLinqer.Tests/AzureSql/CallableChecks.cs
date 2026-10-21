using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.AzureSql;

internal static class CallableChecks
{
    internal static void RunLocal(Action<string, Action> local, Action<bool, string> require)
    {
        local("typed callable SQL syntax and real SqlClient parameter binding", () =>
        {
            using var ids = new DataTable(); ids.Columns.Add("Id", typeof(int)); ids.Rows.Add(3);
            var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"),
                DbFlavor.AzureSql, new ConnectorOptions { DefaultSchema = "probe" });
            var query = NativeCall.Call(new NativeCall.Args { Ids = ids, Counter = 5, Amount = null, Stamp = DateTime.UtcNow }).Render(connector);
            using var command = new SqlCommand();
            typeof(BaseConnector).GetMethod("ImportFromTempQueryData", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(connector, new object[] { command, query });
            LocalChecks.Parse(command.CommandText);
            LocalChecks.Parse(query.GetTextWithParameterValues());
            var parameters = command.Parameters.Cast<Microsoft.Data.SqlClient.SqlParameter>().ToArray();
            require(command.CommandText.Contains("[probe].[TypedCallable]") && parameters.Length == 8, "Callable command differs");
            require(parameters.Count(p => p.Direction == ParameterDirection.Output) == 4
                && parameters.Count(p => p.Direction == ParameterDirection.InputOutput) == 1, "Output bindings differ");
            var tvp = parameters.Single(p => p.SqlDbType == SqlDbType.Structured);
            require(tvp.TypeName == "probe.IdList" && ReferenceEquals(tvp.Value, ids), "TVP binding differs");
            var amount = parameters.Single(p => p.SqlDbType == SqlDbType.Decimal);
            require(amount.Value == DBNull.Value && amount.Precision == 12 && amount.Scale == 2, "Typed decimal NULL lost metadata");
            var stamp = parameters.Single(p => p.SqlDbType == SqlDbType.DateTime2);
            require(stamp.Scale == 7, "Timestamp scale changed");
            var message = parameters.Single(p => p.SqlDbType == SqlDbType.NVarChar);
            require(message.Direction == ParameterDirection.Output && message.Size == 80, "Output string binding differs");
            var letter = parameters.Single(p => p.SqlDbType == SqlDbType.NChar);
            require(letter.Direction == ParameterDirection.Output && letter.Size == 1, "Character output buffer differs");
        });
        local("typed callable live procedure parses", () => LocalChecks.Parse(Procedure("probe")));
    }

    [SqlProcedure("TypedCallable")]
    public sealed class NativeCall : SqlLinqerCallable<NativeCall, NativeCall.Args, NativeCall.Result>
    {
        public class Args
        {
            [SqlParameter(TypeName = "probe.IdList")] public DataTable Ids { get; set; }
            [SqlParameter(Precision = 12, Scale = 2)] public decimal? Amount { get; set; }
            [SqlParameter(SqlDbType = SqlDbType.DateTime2, Scale = 7)] public DateTime Stamp { get; set; }
            public int Counter { get; set; }
        }
        public class Result
        {
            [SqlOutputParameter] public char Letter { get; set; }
            [SqlOutputParameter] public int Counter { get; set; }
            [SqlOutputParameter] public int Total { get; set; }
            [SqlOutputParameter(Size = 80)] public string Message { get; set; }
            [SqlReturnValue] public int ReturnCode { get; set; }
        }
    }

    internal static string Procedure(string schema) => $"""
        CREATE PROCEDURE [{schema}].[TypedCallable]
            @MinId int, @Counter int OUTPUT, @Total int OUTPUT,
            @Message nvarchar(80) OUTPUT, @Name nvarchar(80) = N'default'
        AS BEGIN SET NOCOUNT OFF;
            SET @Counter = @Counter + 1;
            SET @Total = (SELECT COUNT(*) FROM [{schema}].[Rows] WHERE Id >= @MinId);
            SET @Message = @Name;
            SELECT Id, Name FROM [{schema}].[Rows] WHERE Id >= @MinId ORDER BY Id;
            SELECT CAST(@Total AS bigint) AS Total;
            RETURN 17;
        END
        """;

    internal static void RunLive(BaseConnector connector, SqlConnection connection, Action<string, Action> live)
    {
        live("typed callable result sets defaults outputs and return code", () =>
        {
            var args = new LiveCall.Args { MinId = 0, Counter = 5 };
            var result = LiveSuite.Valid(LiveCall.Call(args).Execute(connector));
            LiveSuite.Require(result.Rows.Count == result.Total && result.Totals.Single() == result.Total
                && result.Counter == 6 && result.Message == "default" && result.ReturnCode == 17 && args.Counter == 5,
                "Typed result contract differs");
        });
        live("typed callable caller transaction and explicit NULL", () =>
        {
            using var transaction = connection.BeginTransaction();
            var result = LiveSuite.Valid(LiveCall.Call(new LiveCall.Args
                { MinId = int.MaxValue, Counter = 1, Name = new SqlArgument<string>(null) }).Execute(connector, transaction));
            LiveSuite.Require(result.Rows.Count == 0 && result.Total == 0 && result.Message == null
                && result.Counter == 2 && transaction.Connection == connection, "Empty rows, NULL, or transaction ownership differs");
            transaction.Rollback();
        });
    }

    [SqlProcedure("TypedCallable")]
    public sealed class LiveCall : SqlLinqerCallable<LiveCall, LiveCall.Args, LiveCall.Result>
    {
        public class Args
        {
            public int MinId { get; set; }
            public int Counter { get; set; }
            [SqlParameter(Size = 80)] public SqlArgument<string> Name { get; set; }
        }
        public class Result
        {
            [SqlResultSet(0)] public List<ProcedureRow> Rows { get; set; }
            [SqlResultSet(1)] public List<long> Totals { get; set; }
            [SqlOutputParameter] public int Counter { get; set; }
            [SqlOutputParameter] public int Total { get; set; }
            [SqlOutputParameter(Size = 80)] public string Message { get; set; }
            [SqlReturnValue] public int ReturnCode { get; set; }
        }
    }
}
