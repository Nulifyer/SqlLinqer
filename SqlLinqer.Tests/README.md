# SqlLinqer tests

Run the complete repeatable suite from the repository root:

```sh
bash TestDBs/test-containers.sh
```

This is the main test entry point. It builds the shared .NET 10 executable once, starts disposable PostgreSQL, MySQL, and SQL Server databases, and runs every suite.
The tests run together in a .NET container on an isolated network, without published ports.
The runner saves one result log, reports each suite, and removes all test containers and anonymous volumes on success or failure.
Any failing suite produces a nonzero exit code. Later suites still run after an earlier suite fails.
Separate live Azure SQL validation is optional.

The full run includes:

- Regression checks for SQL rendering, typed models, and connector behavior.
- SQLite and DuckDB database checks.
- Shared SQL Server/Azure SQL provider checks and live SQL Server fixtures, including typed callables.
- PostgreSQL and MySQL integration fixtures.

The current SQL Server fixtures expose error `1205` in the existing generated-type `ExecuteTvp` insert path.
That remains a failure in the full result. Typed callable and existing-table-type procedure checks run before it.

## Container options

The runner needs .NET 10 and an accessible Docker or Podman engine:

```sh
bash TestDBs/test-containers.sh --engine docker
# Run from your terminal if Docker requires sudo:
bash TestDBs/test-containers.sh --engine docker --sudo
bash TestDBs/test-containers.sh --engine podman
# Run the tests on the host with random localhost database ports:
bash TestDBs/test-containers.sh --host
```

Only container commands use sudo. `--containerized` explicitly selects the default isolated execution mode.
Override `SQLLINQER_POSTGRES_IMAGE`, `SQLLINQER_MYSQL_IMAGE`, `SQLLINQER_SQLSERVER_IMAGE`, or `SQLLINQER_RUNTIME_IMAGE` to select another image.
SQL Server images must include `sqlcmd`.

## Run focused or local checks

All suites use the same executable:

```sh
# Local checks only, without Docker or server connections:
dotnet run --project SqlLinqer.Tests
# Select one suite or repeat --suite:
dotnet run --project SqlLinqer.Tests -- --suite regression
dotnet run --project SqlLinqer.Tests -- --suite sqlite --suite duckdb
dotnet run --project SqlLinqer.Tests -- --suite sql-server
dotnet run --project SqlLinqer.Tests -- --help
```

The default includes regression, SQLite, DuckDB, and offline SQL Server/Azure SQL provider/parser checks.
`--suite sql-server` uses the same fixtures for both flavors; there is no separate Azure suite.
Suite code lives in `Regression`, `Sqlite`, `DuckDb`, `SqlServer`, and `Integration`.
SQLite and DuckDB share `Regression/CompatibleDatabaseTests.cs`.

## Supply database endpoints directly

With existing disposable databases, set `SQLLINQER_TEST_POSTGRES`, `SQLLINQER_TEST_MYSQL`, and `SQLLINQER_TEST_SQLSERVER`, then run:

```sh
dotnet run --project SqlLinqer.Tests -- --all
```

`--all` selects every suite and requires the database endpoints. It cannot be combined with `--temp-only`.
PostgreSQL and MySQL database names must start with `sqllinqer_integration_`; their fixtures reset tables between checks.
SQL Server must select a database other than `master` and require encryption.
Local SQL Server containers may use `TrustServerCertificate=true` for their self-signed certificate.
The SQL Server fixtures create and clean up their own schema.

To run only the shared server fixtures against SQL Server:

```sh
dotnet run --project SqlLinqer.Tests -- --suite sql-server --require-live
```

For an optional actual Azure endpoint, set `SQLLINQER_AZURE_SQL_CONNECTION_STRING` and select the same suite with `--azure`:

```sh
dotnet run --project SqlLinqer.Tests -- --suite sql-server --azure --require-live
```

Azure mode retains certificate validation and Azure engine identity checks.
`--live` allows an explicit skip when the selected endpoint is absent; `--require-live` makes absence a failure.
A skipped live check is never counted as a pass. `--temp-only` runs the temporary-table subset and omits procedures and TVPs.
See the [SQL Server/Azure SQL guide](SqlServer/README.md) for endpoint and permission details.

See the [SQLite guide](Sqlite/README.md) for minimum-engine validation and the [DuckDB guide](../docs/DUCKDB.md) for provider coverage.

## Rendering benchmark

```sh
dotnet run -c Release --project SqlLinqer.Tests -- --benchmark-render-only
dotnet run -c Release --project SqlLinqer.Tests -- --suite regression --benchmark-render
```

Benchmarks report measurements and do not make timing assertions.
