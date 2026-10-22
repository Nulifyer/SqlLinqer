using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.SqlServer;

internal static class SqlServerSuite
{
    internal static int Run(bool live = false, bool requireLive = false, bool tempOnly = false, DbFlavor flavor = DbFlavor.SqlServer)
    {
        string engine = flavor == DbFlavor.SqlServer ? "SQL Server" : "Azure SQL";
        string connectionVariable = flavor == DbFlavor.SqlServer ? "SQLLINQER_TEST_SQLSERVER" : "SQLLINQER_AZURE_SQL_CONNECTION_STRING";
        int localPassed = 0, localFailed = 0, livePassed = 0, liveFailed = 0;
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        void Local(string name, Action run)
        {
            try { run(); localPassed++; Console.WriteLine("PASS local " + name); }
            catch (Exception error) { localFailed++; Console.Error.WriteLine("FAIL local " + name + ": " + error); }
        }
        void Live(string name, Action run)
        {
            run(); livePassed++; Console.WriteLine("PASS live " + name);
        }

        foreach (string authentication in new[] { "Sql Password", "Active Directory Default", "Active Directory Interactive", "Active Directory Managed Identity" })
            Local("SqlClient factory preserves " + authentication, () =>
            {
                var original = new SqlConnectionStringBuilder
                {
                    DataSource = "tcp:example.database.windows.net,1433",
                    InitialCatalog = "example",
                    Authentication = Enum.Parse<SqlAuthenticationMethod>(authentication.Replace(" ", "")),
                    Encrypt = SqlConnectionEncryptOption.Mandatory,
                    TrustServerCertificate = false,
                    ApplicationName = "SqlLinqer Azure tests",
                    ConnectRetryCount = 0,
                    ConnectTimeout = 91,
                    CommandTimeout = 99,
                };
                if (authentication == "Sql Password")
                {
                    original.UserID = "example"; original.Password = "sample;=\"' value ";
                }
                if (authentication == "Active Directory Managed Identity") original.UserID = "example-client-id";
                string settings = original.ConnectionString;
                var connector = new ConnectionFactoryConnector(() => new SqlConnection(settings), DbFlavor.AzureSql);
                using var copy = (SqlConnection)connector.CreateConnection();
                var actual = new SqlConnectionStringBuilder(copy.ConnectionString);
                Require(copy.State == ConnectionState.Closed && connector.DbFlavor == DbFlavor.AzureSql, "Connection type, state or flavor changed");
                Require(actual.ConnectTimeout == 91, "Factory login timeout changed");
                Require(actual.EquivalentTo(original), "Factory connection settings changed");
                Require(!connector.JsonResultsRowByRow(), "Azure must assemble SQL Server JSON chunks");
            });
        foreach (string timeout in new[] { "Connect Timeout", "Connection Timeout" })
            Local("SqlClient accepts timeout alias " + timeout, () =>
            {
                string settings = $"Server=example;{timeout}=91;Encrypt=true;TrustServerCertificate=false";
                var connector = new ConnectionFactoryConnector(() => new SqlConnection(settings), DbFlavor.AzureSql);
                using var copy = (SqlConnection)connector.CreateConnection();
                Require(copy.ConnectionTimeout == 91, "Factory timeout alias changed");
            });
        Local("matching Azure authentication extension is deployed", () =>
        {
            var extension = Assembly.Load("Microsoft.Data.SqlClient.Extensions.Azure");
            Require(extension.GetName().Version.Major == 7, "Unexpected Azure extension assembly");
        });
        LocalChecks.Run(Local, Require);
        CallableChecks.RunLocal(Local, Require);

        string liveConnectionString = Environment.GetEnvironmentVariable(connectionVariable);
        if (!live)
        {
            Console.WriteLine($"SKIP live {engine}: use --live or --require-live to enable database execution.");
        }
        else if (string.IsNullOrWhiteSpace(liveConnectionString))
        {
            Console.WriteLine($"SKIP live {engine}: {connectionVariable} is not set. No live {engine} validation performed.");
            if (requireLive) { liveFailed++; Console.Error.WriteLine($"FAIL required live {engine} endpoint is absent."); }
        }
        else if (localFailed == 0)
        {
            try
            {
                LiveSuite.Run(liveConnectionString, tempOnly, Live, flavor);
            }
            catch (Exception error)
            {
                liveFailed++;
                // Never log SQL text, connection strings, provider messages, identity details or tokens.
                SqlException sqlError = null;
                var exceptionKinds = new List<string>();
                for (var cause = error; cause != null; cause = cause.InnerException)
                {
                    exceptionKinds.Add(cause.GetType().Name);
                    if (cause is SqlException sql) sqlError ??= sql;
                }
                var fixtureFrame = new System.Diagnostics.StackTrace(error, true).GetFrames()?
                    .FirstOrDefault(frame => frame.GetMethod()?.DeclaringType == typeof(LiveSuite)
                        || frame.GetMethod()?.DeclaringType?.DeclaringType == typeof(LiveSuite));
                var origin = new System.Diagnostics.StackTrace(error.GetBaseException(), true).GetFrames()?
                    .Take(4).Select(frame => $"{frame.GetMethod()?.DeclaringType?.FullName}.{frame.GetMethod()?.Name}:{frame.GetFileLineNumber()}");
                Console.Error.WriteLine($"FAIL live {engine} check '{LiveSuite.CurrentCheck}': {error.GetType().Name}" +
                    $"; exception types={string.Join(" -> ", exceptionKinds)}" +
                    (sqlError == null ? "" : $"; SqlException Number={sqlError.Number}, State={sqlError.State}, Class={sqlError.Class}") +
                    (fixtureFrame == null ? "" : $"; fixture line={fixtureFrame.GetFileLineNumber()}") +
                    (origin == null ? "" : $"; origin={string.Join(" -> ", origin)}"));
            }
        }
        else Console.WriteLine($"SKIP live {engine}: local provider tests failed.");

        Console.WriteLine($"Local: {localPassed} passed, {localFailed} failed. Live: {livePassed} passed, {liveFailed} failed.");
        return localFailed + liveFailed == 0 ? 0 : 1;
    }
}
