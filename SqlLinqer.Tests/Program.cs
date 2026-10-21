using SqlLinqer.Tests.AzureSql;
using SqlLinqer.Tests.DuckDb;
using SqlLinqer.Tests.Integration;
using SqlLinqer.Tests.Regression;
using SqlLinqer.Tests.Sqlite;

const string usage = """
    Usage: dotnet run --project SqlLinqer.Tests -- [options]

      --suite <name>           Run regression, sqlite, duckdb, azure-sql, or integration.
                              Repeat to select multiple suites. Default: all local suites.
      --live                  Enable optional live Azure SQL checks.
      --require-live          Fail if the Azure SQL endpoint is absent.
      --temp-only             Limit live Azure SQL checks to temporary tables.
      --benchmark-render      Measure rendering after regression checks.
      --benchmark-render-only Measure rendering without running tests.
      --help                  Show this help.

    integration requires SQLLINQER_TEST_POSTGRES and SQLLINQER_TEST_MYSQL.
    Live Azure SQL requires SQLLINQER_AZURE_SQL_CONNECTION_STRING.
    """;

string[] available = ["regression", "sqlite", "duckdb", "azure-sql", "integration"];
var selected = new HashSet<string>(StringComparer.Ordinal);
bool live = false, requireLive = false, tempOnly = false;
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
                error = "--suite requires regression, sqlite, duckdb, azure-sql, or integration.";
            else selected.Add(args[i]);
            break;
        case "--live": live = true; break;
        case "--require-live": live = true; requireLive = true; break;
        case "--temp-only": tempOnly = true; break;
        case "--benchmark-render": benchmark = true; break;
        case "--benchmark-render-only": benchmarkOnly = true; break;
        default: error = $"Unknown option: {args[i]}"; break;
    }
    if (error != null) break;
}
if (selected.Count == 0)
    selected.UnionWith(benchmarkOnly ? ["regression"] : available[..4]);
if (tempOnly && !live) error = "--temp-only requires --live or --require-live.";
if (live && !selected.Contains("azure-sql")) error = "Live Azure SQL options require the azure-sql suite.";
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
            "azure-sql" => AzureSqlSuite.Run(live, requireLive, tempOnly),
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
