# Compare .NET query libraries

Run equivalent reads with SqlLinqer, Dapper, SqlKata with Dapper, LINQ to DB, and EF Core against the same SQLite database.
Every worker validates its result against independently generated fixture values before its measurements are accepted.
This suite measures complete query operations, including query construction, connection opening, mapping, consumption, and disposal.

Requires .NET 10 and Python 3. No database server is required.
Run commands from the repository root.

## Check correctness first

```sh
python3 SqlLinqer.Comparisons/run.py --verify \
  --output TestResults/comparisons-verify.json
python3 SqlLinqer.Comparisons/check-runner.py
```

`--verify` uses fixtures with 0, 1, and 37 customers, a seven-row page, and a five-row offset.
It runs all selected libraries and workloads with one warmup and one measured operation.
This covers empty results, empty pages with a nonzero total, populated graphs, nulls, Unicode, and empty child collections.
These short correctness runs are not performance evidence.
The runner checks also verify rejected arguments, fixture ownership, and detection of corrupted scalar and child data.

## Collect repeated measurements

```sh
python3 SqlLinqer.Comparisons/run.py \
  --rows 1000 10000 --iterations 5 --operations 5 --warmup 5 \
  --output TestResults/comparisons-sqlite.json
```

The runner restores locked dependencies and builds Release once.
It creates temporary file databases through ADO.NET outside the measurement workers and removes them when it exits.
Workers run sequentially in separate processes, in a deterministically shuffled order for each fixture size.
Each worker performs a full preflight query with SQL capture, then the requested full-workload warmups before measurement.
Tiered compilation is disabled for all workers to reduce changes in JIT behavior during short measurements.

The output retains every sample, actual executed SQL and parameter bindings, command counts, package lock, loaded assembly versions,
SDK and runtime, source commit and code hash, host information, run options, and completion status.
An interrupted or failed run is not a completed comparison. Check `status` before using a report.
The runner refuses to replace an existing report unless you pass `--overwrite`.

For a focused run:

```sh
python3 SqlLinqer.Comparisons/run.py \
  --rows 10000 --libraries sqllinqer dapper linq2db \
  --workloads graph page --iterations 5 \
  --output TestResults/comparisons-graphs.json
```

Use `--width` to change the name payload length, `--page-size` and `--offset` to change the page,
and `--timeout` to bound each worker in seconds. The default width adds 128 ASCII characters to each customer's name.
See [recorded results](results/README.md) for one measured baseline and its limits.

Generate a self-contained interactive report from a completed measurement run:

```sh
python3 SqlLinqer.Comparisons/report.py TestResults/comparisons-sqlite.json \
  --output TestResults/comparisons-sqlite.html
```

The HTML lets you switch fixture sizes, workloads, and time or allocation charts.
Its table includes the observed timing range and command counts.

## Equivalent workloads

All queries filter `Active = 1`, select the same root columns, and order roots by their unique ID.
Two of every three fixture customers match the filter.
The three tables contain customers, zero to four orders per customer, and zero to three labels per customer.
Child collections are ordered by ID. Nullable notes and names contain escaped text and Unicode.
Rows are inserted in reverse order, and child foreign-key columns are indexed.

- `flat` buffers the selected customers and their scalar fields.
- `graph` buffers those customers with both independent child collections and every child scalar field.
- `page` buffers a graph page and returns the total number of matching customers before paging.
- `stream` consumes the flat result through async enumeration without retaining previous rows.

The same allocation-free fingerprint work runs inside every timed operation.
It includes every selected value, string character, collection boundary, and result position.
Validation outside timing checks this fingerprint, root and child counts, root ID sum, and page total against the fixture rules.
The parent runner also checks equivalent fingerprints across libraries.

## Query strategies

The adapters use public library APIs and the same `Microsoft.Data.Sqlite` provider.
The package lock pins Dapper 2.1.89, SqlKata.Execution 4.0.1, LINQ to DB 6.5.0, and EF Core SQLite 10.0.13.
SqlLinqer comes from the local project reference. Its working-tree identity is recorded with each run.

- `sqllinqer` uses mapped member expressions and database JSON. Graph reads use one command; a page with its total uses two.
- `dapper` uses parameterized SQL. Graph reads batch three result sets into one command, then attach children through a dictionary.
  A page adds its count to that batch. Flat streaming uses `QueryUnbufferedAsync`.
- `sqlkata` builds root and child queries through `SqlKata.Execution`. Children use a root-key subquery, avoiding per-parent queries.
  Graph reads use three commands; the page total adds a fourth.
  Streaming explicitly composes the SqlKata compiler with Dapper's `QueryUnbufferedAsync`, since `GetAsync` buffers its results.
- `linq2db` uses mapped associations, ordered `LoadWith`, and the library's default eager-loading strategy.
  Captured commands show the actual strategy. This adapter does not compare every available eager-loading strategy.
- `ef-single` uses queries without tracking and explicitly selects `AsSingleQuery` for both collection includes.
- `ef-split` uses the same queries without tracking and selects `AsSplitQuery` for the collection includes.
  EF may omit child commands for an empty root result. Flat workloads are identical between the two EF variants.

ADO.NET commands are counted at execution. A batched command can contain several statements and result sets.
Command count is not a statement count or a measure of database work.
Executed SQL is captured outside the timed region. Counting remains enabled for all libraries during measurement.

The model classes have SqlLinqer and LINQ to DB mapping attributes, and EF's model is configured explicitly.
All adapters return the same classes. Model constructors initialize the same empty collections in every case.
Connectors, compiler instances, EF options, and LINQ to DB options are created before warmup and reused.
Each operation creates and disposes its connection or context. Connection pooling is disabled for every adapter.
No adapter uses explicit compiled queries, a compiled EF model, context pooling, or result caching.
The libraries' normal warm metadata and query caches remain enabled.

## Reading the measurements

Each raw sample contains the mean elapsed time and allocated bytes per operation within one worker.
Summary medians, minima, and maxima are across those independent worker samples.
Five operations inside one worker are not five independent process samples.
First-row latency measures when the caller can access the first model, before fingerprinting that model.
For buffered queries this occurs after the complete list loads. Empty results have no first-row measurement.

Allocations use process-wide `GC.GetTotalAllocatedBytes`, including work after async continuations move threads.
They measure managed allocations, not retained memory, process RSS, SQLite native memory, or database server memory.
Garbage collection counts cover the timed interval. A full collection runs before timing.
Result validation and fixture generation occur outside timing, except for the identical fingerprint consumption work.

These results are a local SQLite baseline with warm caches and low child fanout.
They do not measure network bytes, remote round trips, server CPU, cold startup, writes, high concurrency, or large child collections.
The runner does not flush the OS file cache or isolate the machine from other applications.
[Microsoft.Data.Sqlite async methods execute synchronously](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async),
so this is not evidence about asynchronous server I/O.
Extend the workloads and measure your deployment provider before making a performance claim.

The separate [flat-read investigation](../SqlLinqer.Comparisons.Investigations/README.md) compares the existing native and JSON readers with controlled flat DTOs.
It excludes query construction and investigates the read path without changing production behavior.

## Reference APIs

- [Dapper multi-mapping and multiple results](https://github.com/DapperLib/Dapper)
- [SqlKata query execution](https://sqlkata.com/docs/execution)
- [LINQ to DB mappings and queries](https://github.com/linq2db/linq2db)
- [EF Core single and split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries)
- [SqlLinqer streaming implementation](../docs/contributing/implementation-notes.md#streaming-implementation)
