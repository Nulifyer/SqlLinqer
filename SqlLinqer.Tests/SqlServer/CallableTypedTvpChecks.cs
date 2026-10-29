using System.Data;
using Microsoft.Data.SqlClient;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.SqlServer;

internal static partial class CallableChecks
{
    private static string TypedTvpType(string schema) => $"CREATE TYPE [{schema}].[CallableObjectRows] AS TABLE (row_id int NOT NULL, Amount decimal(18,4) NULL, Status int NULL, Payload varbinary(max) NULL);";
    private static string TypedTvpProcedure(string schema) => $"""
        CREATE PROCEDURE [{schema}].[CallableTypedTvp]
            @Rows [{schema}].[CallableObjectRows] READONLY, @Multiple bit = 0
        AS BEGIN SET NOCOUNT ON;
            SELECT row_id AS Id, Amount, Status, Payload FROM @Rows ORDER BY row_id;
            IF @Multiple = 1 SELECT COUNT_BIG(*) AS Total FROM @Rows;
            RETURN 23;
        END
        """;

    private static void RunTypedTvpLocal(Action<string, Action> local, Action<bool, string> require)
    {
        local("typed procedure TVPs use insert model mapping and real SqlClient Structured bindings", () =>
        {
            LocalChecks.Parse(TypedTvpType("probe")); LocalChecks.Parse(TypedTvpProcedure("probe"));
            foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            {
                var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"), flavor,
                    new ConnectorOptions { DefaultSchema = "probe" });
                var query = new TypedTvpCall<TypedTvpRow[]>().Render(new TypedTvpArgs<TypedTvpRow[]> { Rows = new[] { new TypedTvpRow { Id = 42 } } }, connector);
                LocalChecks.Parse(query.Text);
                using var command = new SqlCommand();
                typeof(BaseConnector).GetMethod("ImportFromTempQueryData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(connector, new object[] { command, query });
                var parameter = command.Parameters.Cast<SqlParameter>().Single(p => p.SqlDbType == SqlDbType.Structured);
                using var table = (DataTable)parameter.Value;
                require(parameter.TypeName == "probe.CallableObjectRows" && table.Columns.Count == 4
                    && (int)table.Rows[0]["row_id"] == 42 && table.Columns["Status"].DataType == typeof(int), "Typed TVP binding lost schema or mapped values");
            }
        });
    }

    private static void RunTypedTvpLive(BaseConnector connector, SqlConnection connection, Action<string, Action> live)
    {
        live("typed procedure TVPs accept models and collections for single and multiple results sync and async", () =>
        {
            var schema = connector.Options.DefaultSchema;
            using (var create = new SqlCommand(TypedTvpType(schema), connection)) create.ExecuteNonQuery();
            try
            {
                using (var create = new SqlCommand(TypedTvpProcedure(schema), connection)) create.ExecuteNonQuery();
                var row = new TypedTvpRow { Id = 42, Amount = 12.3400m, Status = TypedTvpStatus.Active, Payload = new byte[] { 1, 2 } };
                var expected = new TypedTvpResult { Id = 42, Amount = 12.34m, Status = TypedTvpStatus.Active, Payload = new byte[] { 1, 2 } };
                Verify(row, [expected]); Verify(new[] { row }, [expected]); Verify(new List<TypedTvpRow> { row }, [expected]);
                Verify(Enumerable.Repeat(row, 1), [expected]); Verify(Array.Empty<TypedTvpRow>(), []); Verify<TypedTvpRow[]>(null, []);
                // Different values, nulls, duplicate rows, and database ordering must all survive the full round trip.
                Verify(new[] { row, new TypedTvpRow { Id = 1 }, row }, [new TypedTvpResult { Id = 1 }, expected, expected]);
                var batch = Enumerable.Range(0, 256).Select(i => new TypedTvpRow
                {
                    Id = 1000 + i,
                    Amount = i / 100m,
                    Status = TypedTvpStatus.Active,
                    Payload = Enumerable.Repeat((byte)(i % 251), 4096).ToArray()
                }).ToArray();
                var expectedBatch = Enumerable.Range(0, 256).Select(i => new TypedTvpResult
                {
                    Id = 1000 + i,
                    Amount = i / 100m,
                    Status = TypedTvpStatus.Active,
                    Payload = Enumerable.Repeat((byte)(i % 251), 4096).ToArray()
                }).ToArray();
                Verify(batch, expectedBatch);
                using var typeCheck = new SqlCommand($"SELECT TYPE_ID(N'{schema}.CallableObjectRows')", connection);
                LiveSuite.Require(typeCheck.ExecuteScalar() != DBNull.Value, "Procedure calls removed the caller's table type");
                void Verify<T>(T rows, TypedTvpResult[] expectedRows)
                {
                    foreach (bool asynchronous in new[] { false, true })
                    {
                        var input = new TypedTvpArgs<T> { Rows = rows };
                        var normal = new TypedTvpCall<T>();
                        var single = asynchronous ? normal.ExecuteAsync(input, connector).GetAwaiter().GetResult() : normal.Execute(input, connector);
                        var result = single.GetResultOrThrow();
                        AssertRows(result, expectedRows);
                        LiveSuite.Require(single.ReturnCode == 23, "Typed TVP single return code differs");
                        input.Multiple = true;
                        var multi = new TypedTvpMulti<T>();
                        var response = asynchronous ? multi.ExecuteAsync(input, connector).GetAwaiter().GetResult() : multi.Execute(input, connector);
                        var tables = response.GetResultOrThrow();
                        AssertRows(tables.Rows, expectedRows);
                        LiveSuite.Require(tables.Totals.Single() == expectedRows.Length && response.ReturnCode == 23,
                            "Typed TVP multi totals or return code differ");
                    }
                }
                static void AssertRows(IReadOnlyList<TypedTvpResult> actual, IReadOnlyList<TypedTvpResult> expectedRows)
                {
                    LiveSuite.Require(actual.Count == expectedRows.Count, "TVP round trip lost or added rows");
                    for (int i = 0; i < expectedRows.Count; i++)
                    {
                        var expectedRow = expectedRows[i]; var actualRow = actual[i];
                        LiveSuite.Require(actualRow.Id == expectedRow.Id && actualRow.Amount == expectedRow.Amount && actualRow.Status == expectedRow.Status
                            && (actualRow.Payload == null ? expectedRow.Payload == null : expectedRow.Payload != null && actualRow.Payload.SequenceEqual(expectedRow.Payload)),
                            $"TVP round trip changed row {i}, its order, nulls, or mapped values");
                    }
                }
            }
            finally
            {
                using var drop = new SqlCommand($"DROP PROCEDURE IF EXISTS [{schema}].[CallableTypedTvp]; DROP TYPE [{schema}].[CallableObjectRows];", connection);
                drop.ExecuteNonQuery();
            }
        });
    }

    public enum TypedTvpStatus { Inactive, Active }
    public sealed class TypedTvpRow
    {
        [SqlColumn("row_id")] public int Id { get; set; }
        public decimal? Amount { get; set; }
        public TypedTvpStatus? Status { get; set; }
        public byte[] Payload { get; set; }
        [SqlIgnore] public object Ignored { get; set; }
        [SqlAutoInsertExclude] public object LocalOnly { get; set; }
    }
    public sealed class TypedTvpResult
    {
        public int Id { get; set; }
        public decimal? Amount { get; set; }
        public TypedTvpStatus? Status { get; set; }
        public byte[] Payload { get; set; }
    }
    public sealed class TypedTvpArgs<T>
    {
        [SqlParameter(TypeName = "CallableObjectRows")] public T Rows { get; set; }
        public bool Multiple { get; set; }
    }
    [SqlProcedure("CallableTypedTvp")]
    public sealed class TypedTvpCall<T> : SqlLinqerCallable<TypedTvpArgs<T>, TypedTvpResult> { }
    [SqlProcedure("CallableTypedTvp")]
    public sealed class TypedTvpMulti<T> : SqlLinqerMultiCallable<TypedTvpArgs<T>, (TypedTvpResult[] Rows, IReadOnlyList<long> Totals)> { }
}
