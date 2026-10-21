using SqlLinqer.Tests.SqlServer;
using SqlLinqer.Tests.DuckDb;
using SqlLinqer.Tests.Integration;
using SqlLinqer.Tests.Regression;
using SqlLinqer.Tests.Sqlite;

const string usage = """
    Usage: dotnet run --project SqlLinqer.Tests -- [options]

      --all                   Run every suite and require all database endpoints.
      --suite <name>           Run regression, sqlite, duckdb, sql-server, or integration.
                              Repeat to select multiple suites. Default: all local suites.
      --azure                 Use an Azure endpoint for the shared SQL Server suite.
      --live                  Enable optional live SQL Server or Azure SQL checks.
      --require-live          Fail if a selected SQL Server/Azure SQL endpoint is absent.
      --temp-only             Limit live SQL Server/Azure SQL checks to temporary tables.
      --benchmark-render      Measure rendering after regression checks.
      --benchmark-render-only Measure rendering without running tests.
      --help                  Show this help.

    integration requires SQLLINQER_TEST_POSTGRES and SQLLINQER_TEST_MYSQL.
    Live Azure SQL requires SQLLINQER_AZURE_SQL_CONNECTION_STRING.
    Live SQL Server requires SQLLINQER_TEST_SQLSERVER.
    Run bash TestDBs/test-containers.sh to provision databases and run --all.
    """;

string[] available = ["regression", "sqlite", "duckdb", "sql-server", "integration"];
var selected = new HashSet<string>(StringComparer.Ordinal);
bool live = false, requireLive = false, tempOnly = false;
bool all = false, azure = false;
bool benchmark = false, benchmarkOnly = false;
string error = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help":
        case "-h":
            Console.WriteLine(usage);
            return 0;
        case "--suite":
            if (++i == args.Length || !available.Contains(args[i]))
                error = "--suite requires regression, sqlite, duckdb, sql-server, or integration.";
            else selected.Add(args[i]);
            break;
        case "--all": all = true; live = true; requireLive = true; break;
        case "--azure": azure = true; break;
        case "--live": live = true; break;
        case "--require-live": live = true; requireLive = true; break;
        case "--temp-only": tempOnly = true; break;
        case "--benchmark-render": benchmark = true; break;
        case "--benchmark-render-only": benchmarkOnly = true; break;
        default: error = $"Unknown option: {args[i]}"; break;
    }
    if (error != null) break;
}
if (all) selected.UnionWith(available);
else if (selected.Count == 0)
    selected.UnionWith(benchmarkOnly ? ["regression"] : available[..4]);
if (all && tempOnly) error = "--all cannot be combined with --temp-only.";
if (tempOnly && !live) error = "--temp-only requires --live or --require-live.";
if ((live || azure) && !selected.Contains("sql-server"))
    error = "Live database options require the sql-server suite.";
if ((benchmark || benchmarkOnly) && !selected.Contains("regression"))
    error = "Rendering benchmarks require the regression suite.";
if (benchmarkOnly && (selected.Count != 1 || live || tempOnly || benchmark))
    error = "--benchmark-render-only cannot be combined with other suites or execution options.";
if (error != null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(usage);
    return 2;
}
if (benchmarkOnly)
{
    CompatibleQueryRegressionTests.MeasureRendering();
    return 0;
}

int failed = 0;
foreach (string suite in available.Where(selected.Contains))
{
    Console.WriteLine($"=== {suite} ===");
    var previous = SqlLinqer.Default.Connector;
    int result;
    try
    {
        result = suite switch
        {
            "regression" => RegressionSuite.Run(benchmark),
            "sqlite" => SqliteSuite.Run(),
            "duckdb" => DuckDbSuite.Run(),
            "sql-server" => SqlServerSuite.Run(live, requireLive, tempOnly,
                azure ? SqlLinqer.DbFlavor.AzureSql : SqlLinqer.DbFlavor.SqlServer),
            "integration" => IntegrationSuite.Run(),
            _ => throw new InvalidOperationException($"Unknown suite: {suite}")
        };
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL {suite}: {exception}");
        result = 1;
    }
    finally
    {
        SqlLinqer.Default.Connector = previous;
    }
    if (result != 0) failed++;
    Console.WriteLine($"{suite}: {(result == 0 ? "PASS" : "FAIL")}");
}
Console.WriteLine($"Suites: {selected.Count - failed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
