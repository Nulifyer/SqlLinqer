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
- SQLite and DuckDB scale checks with 10,000 rows, including paging, aggregates, binary payloads, transactions, and concurrent reads.

SQL Server TVP checks cover synchronous and asynchronous inserts, returned keys, output values, and generated-type cleanup.
They also verify caller rollback, server-aborted transactions, cancellation after type creation, and caller database overrides.

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
dotnet run -c Release --project SqlLinqer.Tests -- --suite scale --rows 1000 --iterations 1
dotnet run --project SqlLinqer.Tests -- --help
```

The default includes regression, SQLite, DuckDB, and offline SQL Server/Azure SQL provider/parser checks. Scale checks require explicit selection or `--all`.
`--suite sql-server` uses the same fixtures for both flavors; there is no separate Azure suite.
Suite code lives in `Regression`, `Sqlite`, `DuckDb`, `SqlServer`, `Integration`, and `Scale`.
SQLite and DuckDB share `Regression/CompatibleDatabaseTests.cs`.

Within the SQLite and DuckDB suites, `MappingChecks`, `QueryChecks`, and `WriteChecks` group checks by behavior.
DuckDB command deadline checks live in `ConnectionChecks`. Each provider keeps its model fixtures in `TestModels.cs`.
The suite entry points own provider setup, cleanup, result reporting, and check order.
Check groups receive the connector and any shared fixture values explicitly. Keep stateful checks in their existing execution order.

## Relationship stress checks

`Regression/RelationshipStressChecks.cs` runs through the shared SQL JSON contract checks on every live provider.
It also runs in both SQLite configurations, a file database and shared memory.
Two fixed seeds, `17` and `20261022`, populate seven tables with 32 roots and 40 people.
Projects, tickets, membership links, regions, and categories form paths up to five relationships deep.
The fixture includes self references, repeated types, shared foreign keys, missing targets, Unicode, empty collections, and tied sort values.
Rows are inserted in reverse order where possible.

The expected graphs come from raw fixture records. ADO.NET creates and populates the tables independently of SqlLinqer.
Every selected scalar and relationship is compared after materialization.
Unordered cases compare collection membership. Ordered cases compare the exact sequence at every level.
One deep filter is also checked against a hand-written SQL `EXISTS` query.

Each seed covers:

- Complete graphs using one command, including one-to-one, one-to-many, and many-to-many paths.
- Separate projections of the same relationship with different selected descendants.
- Deep existence and exclusion filters, nullable-key `OR` filters, and filtered page totals.
- Ordered collections with ascending and descending keys and deterministic tie breakers.
- Independent paging at two collection levels, including `DISTINCT` and empty child pages.
- Root paging and empty pages without multiplying or losing the root total.
- Explicit finite cycles that return through a project to its root and owner.
- Deferred streams that retain their graph after the original builder changes.
- Eight parallel graph reads, with up to four readers, and stable repeated rendering.

Graph cases use buffered sync, buffered async, and streamed reads.
The paged-total check requires two commands. Ordinary graph reads require one, guarding against extra per-relationship queries.
These fixtures exercise relationship behavior. They do not prove every combination of SQL expressions or database versions.

The scale suite also measures `nested-mixed-buffered` and `nested-mixed-stream` on SQLite and DuckDB.
Both traverse collection → object → collection over the large row fixture.
The outer collection is limited to 64 members and the innermost collection to three members per parent.
These limits keep the workload bounded when `--rows` increases to one million.
Every sampled result is validated after timing. Managed allocation measurements cover only the calling thread, so async results can omit worker allocations.
No timing threshold is imposed across machines.

```sh
dotnet run -c Release --project SqlLinqer.Tests -- --suite scale --rows 10000 --iterations 3 --results TestResults/scale.json
```

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

See the [SQLite guide](Sqlite/README.md) for minimum-engine validation and the [DuckDB connection notes](../docs/getting-started/connections.md#duckdb) for setup.

## Rendering benchmark

```sh
dotnet run -c Release --project SqlLinqer.Tests -- --benchmark-render-only
dotnet run -c Release --project SqlLinqer.Tests -- --suite regression --benchmark-render
```

Benchmarks report measurements and do not make timing assertions.
See the [scale guide](Scale/README.md) for larger datasets, measurement limits, and JSON result files.
Run `python3 SqlLinqer.Tests/check-runner.py` to check command-line validation and exit codes without server endpoints.

Run `bash TestDBs/test-stable-contracts.sh` to compile the same SQL JSON fixtures against `v2.1.0` and the working library.
The script needs the stable Git tag and writes reference identity, fixture hashes, and both result logs under `/tmp`.
These fixtures check materialization and shared BuildQuery contracts with controlled reader values.
The container suites check actual provider SQL execution.

## Execution mode coverage

The [result conversion matrix](../docs/execution/value-conversions.md) covers signed and unsigned integer widths, decimal and floating-point values,
date/time parsing and formatting, offsets, nulls, GUIDs, enums, characters, booleans, and binary values.
`Regression/ValueConversionRegressionTests.cs` runs generated cases through all three JSON execution modes and both native read modes.
`Regression/ValueConversionProviderChecks.cs` verifies native database values and generated JSON through the shared provider checks.
The matrix includes expected conversion failures and checks range limits, precision, culture, timezone rules, and resource disposal.

The regression suite includes snapshot isolation, repeated data/count compilation, and concurrent rendering across dialects.
It also checks asynchronous command contracts, cancellation, caller transaction ownership, insert batching, and typed callable outputs.
Async checks execute CRUD, JSON results, binary values, nested collections, aggregates, and rollback against temporary SQLite and DuckDB databases.
The integration suite includes asynchronous PostgreSQL/MySQL checks when its disposable endpoints are configured.

The shared provider checks run `Execute`, `ExecuteAsync`, and `ExecuteAsyncStream` against SQLite, DuckDB, PostgreSQL, MySQL, and live SQL Server.
They cover empty results, paging, anonymous projections, aggregates, captured commands, binary values, and large Unicode JSON values.
Streaming checks verify early reader disposal on the same caller connection, caller rollback, cancellation tokens, subsequent execution, and provider error context.
Write and callable checks exercise their supported synchronous and asynchronous methods.

The native SQLite, DuckDB, SQL Server, PostgreSQL, and MySQL suites share SQL JSON contract checks.
They compare plain models with API-attributed models against the same rows, including nested objects, collections, nulls, and escaped text.
The checks exercise API names, ignores, constructors, required members, converters, serializer policies, and metadata caches without changing SQL loading.
They also verify captured queries, concurrent API serialization and SQL loading, and writes through API-attributed models.
The binary fixture crosses Base64 line boundaries and includes SQL NULL values in all three read modes.

The render benchmark reports ordinary rendering allocations, snapshot capture allocations, and rendering allocations from a reused snapshot.
See [query implementation notes](../docs/contributing/implementation-notes.md) for capture and rendering details.
The [execution guide](../docs/execution/sync-and-async.md) covers supported methods and transaction use.
