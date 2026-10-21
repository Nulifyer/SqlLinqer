# Scale checks and measurements

The scale suite uses the shared `SqlLinqer.Tests` executable and disposable SQLite and DuckDB databases.
It does not read server connection variables. Each provider creates and removes its own temporary directory.

`--all`, including the container runner, runs this suite with 10,000 rows and three measurement samples.
The default local run excludes scale checks. Select `--suite scale` to run them separately.

```sh
# Small correctness check:
dotnet run -c Release --project SqlLinqer.Tests -- --suite scale --rows 1000 --iterations 1
# Larger datasets with machine-readable results:
dotnet run -c Release --project SqlLinqer.Tests -- --suite scale --rows 100000 --iterations 5 --results TestResults/scale-100000.json
dotnet run -c Release --project SqlLinqer.Tests -- --suite scale --rows 1000000 --iterations 3 --results TestResults/scale-1000000.json
```

`--rows` accepts 1,000 through 1,000,000. `--iterations` accepts 1 through 20.
These options and `--results` require `--suite scale` or `--all`.
Result files under `TestResults` are ignored by Git.

## Coverage

The suite checks every loaded key and payload, grouped counts and Int64 sums, deep pages, empty pages, and a large parameterized IN filter.
Fixtures include SQL NULL, empty strings, Unicode, quotes, newlines, and null, empty, and populated binary values.
Nested projections explicitly select their relationships. Loading always uses the library-owned SQL model contract.

Insert queries contain at most 2,000 rows and cross a 999-parameter batching budget inside one caller transaction.
Checks cover returned keys, late duplicate failures, atomic rollback, recovery, and caller-owned update/delete transactions.
Twenty-four read tasks use at most four concurrent workers with independent queries and parameters.
The full typed JSON dataset is checked through `Execute`, `ExecuteAsync`, and `ExecuteAsyncStream`.
The stream validates every row and checksum during consumption without retaining the result set.
An asynchronous nested page checks binary payloads and the total before paging. A streamed nested page checks correlated values and binary payloads.
Rendering checks cover all six dialects with 10, 100, and 1,000 predicates.

The normal SQLite and DuckDB suites also check exact and partial batch boundaries and failed insert reuse.
Regression checks render independent queries concurrently across schemas.
Run `python3 SqlLinqer.Tests/check-runner.py` to verify runner arguments, missing endpoints, exit codes, and continued execution after a suite fails.

## Measurement limits

Use Release builds for performance comparisons. Read and rendering measurements have one warmup followed by the requested samples.
The initial insert has one cold sample, including fixture generation and per-batch assertions.
The deep-page measurement includes the empty-page query and state assertions.
Full-result streaming validates rows during consumption to keep retention bounded. Other measured results are validated after timing stops.
Validation failures fail the suite. There are no timing thresholds.

Wall time includes query construction, connection creation, execution, and materialization.
Managed allocation counts cover the calling thread and exclude native memory and provider worker allocations.
Async execution can switch threads, so these scale-suite allocation counts do not measure total async allocations.
Use the [isolated streaming benchmark](../../SqlLinqer.Streaming.Benchmarks/README.md) for process-wide allocation and memory comparisons.
Peak working set covers the whole process and cannot identify one operation's memory use.
Typed JSON projects six columns; the DataTable workload projects two. Their timings do not measure equivalent workloads.

DuckDB uses two execution threads and a 512 MB database memory limit. C# result materialization can use additional memory.
The JSON report records each sample, provider and engine versions, runtime, operating system, architecture, and validation results.
Compare the same configuration on the same machine under similar load.

These tests were adapted from commit `101280a` on `test/full-coverage-performance-20261020`.
The historical benchmark records remain on that branch because they measured an older implementation.
Generate new records when comparing the current JSON pipeline or query compiler.
