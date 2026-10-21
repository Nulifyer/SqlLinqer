# SqlLinqer tests

All automated tests use this .NET 10 executable. Run from the repository root:

```sh
dotnet run --project SqlLinqer.Tests
```

The default run includes regression checks, temporary SQLite and DuckDB databases, and offline Azure SQL provider/parser checks.
It does not connect to PostgreSQL, MySQL, or Azure SQL Database.
Each suite reports its results. Any failing suite makes the process exit with code `1`.
Invalid arguments exit with code `2`.

Select one suite, or repeat `--suite` to select several:

```sh
dotnet run --project SqlLinqer.Tests -- --suite regression
dotnet run --project SqlLinqer.Tests -- --suite sqlite --suite duckdb
dotnet run --project SqlLinqer.Tests -- --suite azure-sql
dotnet run --project SqlLinqer.Tests -- --help
```

Suite code and fixtures live in `Regression`, `Sqlite`, `DuckDb`, `AzureSql`, and `Integration`.
SQLite and DuckDB share the `Regression/CompatibleDatabaseTests.cs` fixture.
The core library retains its own provider-independent dependencies.

## Database suites

For PostgreSQL and MySQL, use the disposable container runner:

```sh
bash TestDBs/test-containers.sh --engine docker
```

The script builds this project and selects `--suite integration`.
It also supports `--sudo`, `--engine podman`, and `--containerized`.
To supply disposable databases directly, set `SQLLINQER_TEST_POSTGRES` and `SQLLINQER_TEST_MYSQL`, then run:

```sh
dotnet run --project SqlLinqer.Tests -- --suite integration
```

Both connection strings must select a database named `sqllinqer_integration_<run id>`.
The suite resets its fixture tables between checks.

For Azure SQL, set `SQLLINQER_AZURE_SQL_CONNECTION_STRING` to an existing disposable Azure SQL Database.
Then require live execution:

```sh
dotnet run --project SqlLinqer.Tests -- --suite azure-sql --require-live
```

`--live` permits an explicit skip when the endpoint is absent. `--require-live` makes that absence a failure.
`--temp-only` requires one of these flags and limits the live suite to temporary tables.
See the [Azure SQL guide](AzureSql/README.md) for connection requirements and cleanup behavior.

See the [SQLite guide](Sqlite/README.md) for minimum-engine validation and [DuckDB guide](../docs/DUCKDB.md) for provider coverage.

## Rendering benchmark

```sh
dotnet run -c Release --project SqlLinqer.Tests -- --benchmark-render-only
dotnet run -c Release --project SqlLinqer.Tests -- --suite regression --benchmark-render
```

Benchmarks report measurements and do not make timing assertions.
