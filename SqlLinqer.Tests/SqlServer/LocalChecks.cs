using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.SqlServer;

internal static class LocalChecks
{
    internal static void Parse(string sql)
    {
        var parser = new TSql160Parser(true, SqlEngineType.SqlAzure);
        using var text = new StringReader(sql);
        parser.Parse(text, out var errors);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join("; ", errors.Select(x => $"SQL parse {x.Number} at {x.Line}:{x.Column}: {x.Message}")));
    }
    internal static void Run(Action<string, Action> local, Action<bool, string> require)
    {
        local("factory preserves token callback credentials and login settings", () =>
        {
            Func<SqlAuthenticationParameters, CancellationToken, Task<SqlAuthenticationToken>> callback = (_, _) =>
                throw new InvalidOperationException("Local tests must never acquire tokens");
            var settings = new SqlConnectionStringBuilder
            {
                DataSource = "example.database.windows.net",
                InitialCatalog = "example",
                ConnectTimeout = 19,
                Encrypt = SqlConnectionEncryptOption.Mandatory,
                TrustServerCertificate = false,
            };
            int calls = 0;
            var connector = new ConnectionFactoryConnector(() =>
            {
                calls++;
                return new SqlConnection(settings.ConnectionString) { AccessTokenCallback = callback };
            }, DbFlavor.AzureSql, new ConnectorOptions { ConnectionTimeout = TimeSpan.FromSeconds(1) });
            using var first = (SqlConnection)connector.CreateConnection();
            using var second = (SqlConnection)connector.CreateConnection();
            require(!ReferenceEquals(first, second) && calls == 2 && ReferenceEquals(first.AccessTokenCallback, callback)
                && ReferenceEquals(second.AccessTokenCallback, callback) && first.ConnectionTimeout == 19
                && first.State == ConnectionState.Closed && connector.DbFlavor == DbFlavor.AzureSql, "Factory changed callback/settings/ownership");
            var tokenConnector = new ConnectionFactoryConnector(() => new SqlConnection(settings.ConnectionString) { AccessToken = "local-placeholder" }, DbFlavor.AzureSql);
            using var tokenConnection = (SqlConnection)tokenConnector.CreateConnection();
            require(tokenConnection.AccessToken == "local-placeholder", "Factory dropped token");
            using var password = new System.Security.SecureString(); password.AppendChar('x'); password.MakeReadOnly();
            var credential = new SqlCredential("local-example", password);
            var credentialConnector = new ConnectionFactoryConnector(() => new SqlConnection(settings.ConnectionString, credential), DbFlavor.AzureSql);
            using var credentialConnection = (SqlConnection)credentialConnector.CreateConnection();
            require(ReferenceEquals(credentialConnection.Credential, credential), "Factory dropped SqlCredential");
        });
        local("provider binds stored procedure typed null and Structured TVP", () =>
        {
            using var ids = new DataTable(); ids.Columns.Add("Id", typeof(int)); ids.Rows.Add(3);
            var query = new StoredProcedureQuery<ProcedureRow>("ReadJson", "probe")
                .AddParameter("name", null, DbType.String).AddTvpParameter("Ids", ids, "probe.IdList").Render(DbFlavor.AzureSql);
            using var command = new SqlCommand();
            // Exercise the actual provider-independent import using real SqlParameter instances without a connection.
            var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"), DbFlavor.AzureSql);
            var import = typeof(BaseConnector).GetMethod("ImportFromTempQueryData", BindingFlags.Instance | BindingFlags.NonPublic);
            import.Invoke(connector, new object[] { command, query });
            require(command.Parameters.Count == 2 && command.Parameters[0].Value == DBNull.Value
                && command.Parameters[0].DbType == DbType.String && command.Parameters[1].SqlDbType == SqlDbType.Structured
                && command.Parameters[1].TypeName == "probe.IdList" && ReferenceEquals(command.Parameters[1].Value, ids), "Provider parameter metadata changed");
            Parse(command.CommandText);
        });
        foreach (var test in QueryCases.All())
        {
            local("Azure T-SQL parser " + test.Name, () => Parse(test.Query.Render(DbFlavor.AzureSql).Text));
            if (test.Query is SqlLinqer.Queries.Core.SelectQuery select)
            {
                local("Azure T-SQL non-JSON parser " + test.Name, () => Parse(select.Render(new ParameterCollection(), DbFlavor.AzureSql, false)));
                local("Azure T-SQL count parser " + test.Name, () => Parse(select.RenderForCount(new ParameterCollection(), DbFlavor.AzureSql)));
            }
        }
        local("Azure T-SQL TVP native type declarations and nullable data", () =>
        {
            var insert = new InsertQuery<AzureTypes>().InsertAuto(LiveSuite.SampleTypes());
            var parameters = new ParameterCollection();
            var tvp = insert.RenderTVP(parameters, DbFlavor.AzureSql, "probe");
            var type = insert.RenderTempType(DbFlavor.AzureSql, tvp.TempTypeId);
            Parse(type.Create); Parse(tvp.Query); Parse(type.Drop);
            require(type.Create.Contains("DECIMAL(38,12)") && type.Create.Contains("DATETIME2(7)")
                && type.Create.Contains("NCHAR(1)"), "TVP generates invalid or lossy native types");
            using var values = (DataTable)((TvpParameter)parameters.GetAll().Single()).Value;
            require(values.Rows[0]["Optional"] == DBNull.Value && values.Columns["OffsetStamp"].DataType == typeof(DateTimeOffset), "TVP nullable/type data differs");
            using var command = new SqlCommand();
            var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"), DbFlavor.AzureSql);
            typeof(BaseConnector).GetMethod("ImportFromTempQueryData", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(connector, new object[] { command, new RenderedQuery(tvp.Query) { Parameters = parameters } });
            require(command.Parameters[0].SqlDbType == SqlDbType.Structured && command.Parameters[0].TypeName == tvp.TempTypeId, "Insert TVP provider binding differs");
        });
        local("SqlClient native type parameter binding", () =>
        {
            var rendered = new InsertQuery<AzureTypes>().InsertAuto(LiveSuite.SampleTypes()).Render(DbFlavor.AzureSql);
            using var command = new SqlCommand();
            var connector = new ConnectionFactoryConnector(() => throw new InvalidOperationException("Must not open"), DbFlavor.AzureSql);
            typeof(BaseConnector).GetMethod("ImportFromTempQueryData", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(connector, new object[] { command, rendered });
            SqlDbType[] expected = { SqlDbType.TinyInt, SqlDbType.SmallInt, SqlDbType.BigInt, SqlDbType.Decimal,
                SqlDbType.Real, SqlDbType.Float, SqlDbType.UniqueIdentifier, SqlDbType.Bit, SqlDbType.NVarChar,
                SqlDbType.DateTime2, SqlDbType.DateTimeOffset, SqlDbType.Time, SqlDbType.VarBinary, SqlDbType.NVarChar, SqlDbType.NChar };
            require(command.Parameters.Cast<SqlParameter>().Select(x => x.SqlDbType).SequenceEqual(expected), "Native parameter types differ");
            require(command.Parameters[8].Value == DBNull.Value, "Nullable parameter changed");
        });
        local("Azure T-SQL suite setup procedures and cleanup", () =>
        {
            Parse(LiveSuite.TempSetup); Parse(LiveSuite.TempCleanup);
            Parse(LiveSuite.ScopedTableSetup("probe")); Parse(LiveSuite.TableProcedure("probe"));
            Parse(LiveSuite.JsonProcedure("probe")); Parse(LiveSuite.ScopedCleanup("probe"));
        });
    }
}
