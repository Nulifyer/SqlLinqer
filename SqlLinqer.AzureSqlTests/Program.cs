using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

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
    Local("SqlClient replication preserves " + authentication, () =>
    {
        var original = new SqlConnectionStringBuilder
        {
            DataSource = "tcp:example.database.windows.net,1433", InitialCatalog = "example",
            Authentication = Enum.Parse<SqlAuthenticationMethod>(authentication.Replace(" ", "")),
            Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = false,
            ApplicationName = "SqlLinqer Azure tests", ConnectRetryCount = 0, ConnectTimeout = 91, CommandTimeout = 99,
        };
        if (authentication == "Sql Password")
        {
            original.UserID = "example"; original.Password = "sample;=\"' value ";
        }
        if (authentication == "Active Directory Managed Identity") original.UserID = "example-client-id";
        using var template = new SqlConnection(original.ConnectionString);
        var connector = new ReplicatorConnector(template, DbFlavor.AzureSql,
            new ConnectorOptions { ConnectionTimeout = TimeSpan.FromSeconds(7) });
        using var copy = (SqlConnection)connector.CreateConnection();
        var actual = new SqlConnectionStringBuilder(copy.ConnectionString);
        Require(copy.State == ConnectionState.Closed && connector.DbFlavor == DbFlavor.AzureSql, "Connection type, state or flavor changed");
        Require(actual.ConnectTimeout == 7, "Timeout was not replaced");
        original.ConnectTimeout = 7;
        Require(actual.EquivalentTo(original), "Connection settings changed beyond timeout");
        Require(!connector.JsonResultsRowByRow(), "Azure must assemble SQL Server JSON chunks");
    });
foreach (string timeout in new[] { "Connect Timeout", "Connection Timeout" })
    Local("SqlClient accepts timeout alias " + timeout, () =>
    {
        using var template = new SqlConnection($"Server=example;{timeout}=91;Encrypt=true;TrustServerCertificate=false");
        var connector = new ReplicatorConnector(template, DbFlavor.AzureSql,
            new ConnectorOptions { ConnectionTimeout = TimeSpan.FromSeconds(8) });
        using var copy = (SqlConnection)connector.CreateConnection();
        Require(copy.ConnectionTimeout == 8, "Duplicate timeout alias survived");
    });
Local("matching Azure authentication extension is deployed", () =>
{
    var extension = Assembly.Load("Microsoft.Data.SqlClient.Extensions.Azure");
    Require(extension.GetName().Version.Major == 7, "Unexpected Azure extension assembly");
});
Local("ReplicatorConnector property authentication limitation is explicit", () =>
{
    const string connectionString = "Server=example;Database=example;Encrypt=true;TrustServerCertificate=false";
    using var tokenTemplate = new SqlConnection(connectionString) { AccessToken = "example-token-not-used-for-a-connection" };
    using var tokenCopy = (SqlConnection)new ReplicatorConnector(tokenTemplate, DbFlavor.AzureSql).CreateConnection();
    Require(string.IsNullOrEmpty(tokenCopy.AccessToken), "Update documentation if token replication changes");
    using var callbackTemplate = new SqlConnection(connectionString)
    {
        AccessTokenCallback = (_, _) => throw new InvalidOperationException("Local tests must never acquire a token"),
    };
    using var callbackCopy = (SqlConnection)new ReplicatorConnector(callbackTemplate, DbFlavor.AzureSql).CreateConnection();
    Require(callbackCopy.AccessTokenCallback == null, "Update documentation if callback replication changes");
});

LocalChecks.Run(Local, Require);
CallableChecks.RunLocal(Local, Require);

bool requireLive = args.Contains("--require-live");
bool tempOnly = args.Contains("--temp-only");
if (args.Any(x => x != "--require-live" && x != "--temp-only"))
{
    Console.Error.WriteLine("Usage: dotnet run --project SqlLinqer.AzureSqlTests -- [--require-live] [--temp-only]");
    return 2;
}
string liveConnectionString = Environment.GetEnvironmentVariable("SQLLINQER_AZURE_SQL_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(liveConnectionString))
{
    Console.WriteLine("SKIP live Azure SQL: SQLLINQER_AZURE_SQL_CONNECTION_STRING is not set. No live Azure validation performed.");
    if (requireLive) { liveFailed++; Console.Error.WriteLine("FAIL required live Azure SQL endpoint is absent."); }
}
else if (localFailed == 0)
{
    try
    {
        LiveSuite.Run(liveConnectionString, tempOnly, Live);
    }
    catch (Exception error)
    {
        liveFailed++;
        // Never log SQL text, connection strings, provider messages, identity details or tokens.
        var sqlError = error as SqlException ?? error.InnerException as SqlException;
        Console.Error.WriteLine($"FAIL live Azure SQL check '{LiveSuite.CurrentCheck}': {error.GetType().Name}" +
            (sqlError == null ? "" : $"; SqlException Number={sqlError.Number}, State={sqlError.State}, Class={sqlError.Class}"));
    }
}
else Console.WriteLine("SKIP live Azure SQL: local provider tests failed.");

Console.WriteLine($"Local: {localPassed} passed, {localFailed} failed. Live: {livePassed} passed, {liveFailed} failed.");
return localFailed + liveFailed == 0 ? 0 : 1;
