[README](../README.md)

# Contributing

## Build and check

Run these commands from the repository root.

`SqlLinqer.Tests.Net10` tests .NET 10 compatibility. `SqlLinqer.Tests.NetFramework48` tests .NET Framework 4.8 compatibility and uses Npgsql 4.1.14.
The library targets .NET Standard 2.0.

```sh
dotnet build SqlLinqer.Tests.Net10/SqlLinqer.Tests.Net10.csproj
dotnet build SqlLinqer.Tests.NetFramework48/SqlLinqer.Tests.NetFramework48.csproj
dotnet run --project SqlLinqer.Tests
```

All automated suites use [SqlLinqer.Tests](../SqlLinqer.Tests/README.md).
For the complete repeatable run, including real PostgreSQL, MySQL, and SQL Server databases:

```sh
bash TestDBs/test-containers.sh
# If Docker requires sudo, run this from your terminal:
bash TestDBs/test-containers.sh --sudo
```

The runner builds once, runs every suite in an isolated container environment, saves one result log, and removes its containers and volumes.
It supports Docker or Podman. Use `--host` to run tests on the host with random localhost database ports.
Failures remain nonzero, including live database and cleanup failures.
Direct Azure SQL testing is optional and uses the same shared server fixtures.

`dotnet run --project SqlLinqer.Tests` runs local regression, SQLite, DuckDB, and offline provider checks without server connections.
Use `-- --suite regression`, `sqlite`, `duckdb`, `sql-server`, or `integration` for focused checks.
The two compatibility test programs require the employee database described in their connection settings.

## Source organization

Keep a cohesive type in one file. Use partials when a large type has distinct capabilities that share its state.
Keep the base type, implemented interfaces, generic constraints, and constructors in the primary file.
Group synchronous and asynchronous methods with the capability and helpers they implement.

Connector partials separate common setup, buffered execution, JSON reads, tabular reads, and streaming.
Typed selects separate query building, buffered execution, and streaming. Inserts separate query building, ordinary execution, and TVP execution.
Projection function definitions live in the projection builder's `.Functions.cs` partial.
Independent mapping algorithms and resource owners remain separate types. File length alone does not require a partial.
