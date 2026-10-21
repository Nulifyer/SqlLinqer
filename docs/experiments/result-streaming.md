# Result streaming experiment

Branch: `feature/result-streaming`, based on `861cd6d`. Changes are uncommitted.

The async streaming implementation reduces peak memory and exposes rows before the full result loads. At one million rows, all measured streaming paths finish faster than their buffered counterparts in the final run. DuckDB process memory still grows with result size, so this experiment establishes bounded managed retention, not a fixed process-memory budget across providers.

## Final measurements

Values are medians of three isolated Release worker processes per mode. Each row has an integer ID, a 256-character string, and a nullable Unicode string. The table uses one million rows. `batch` retains up to 1000 objects. `collect` intentionally retains every streamed object.

| Workload | Mode | Elapsed seconds | Peak process MiB | Sampled peak managed MiB | First object ms |
| --- | --- | ---: | ---: | ---: | ---: |
| SQLite | buffered | 2.926 | 696.7 | 587.4 | 2875.58 |
| SQLite | stream | 1.934 | 84.1 | 8.4 | 1.46 |
| SQLite | batch | 1.927 | 86.4 | 10.0 | 1.54 |
| SQLite | collect | 3.015 | 701.9 | 587.4 | 1.63 |
| DuckDB ordered | buffered | 2.342 | 1223.7 | 587.5 | 2289.86 |
| DuckDB ordered | stream | 1.338 | 498.5 | 8.4 | 94.24 |
| DuckDB ordered | batch | 1.447 | 499.6 | 10.1 | 94.28 |
| DuckDB ordered | collect | 2.537 | 1100.9 | 587.5 | 94.75 |
| DuckDB scan | buffered | 2.461 | 1114.1 | 587.4 | 2409.54 |
| DuckDB scan | stream | 1.570 | 503.3 | 8.4 | 90.87 |
| DuckDB scan | batch | 1.615 | 507.2 | 10.4 | 89.86 |
| DuckDB scan | collect | 2.714 | 1108.2 | 587.4 | 92.23 |
| JSON document on SQLite | buffered | 2.838 | 2130.5 | 2609.5 | 2793.79 |
| JSON document on SQLite | stream | 2.651 | 82.5 | 8.9 | 0.33 |
| JSON document on SQLite | batch | 2.709 | 84.2 | 10.1 | 0.37 |
| JSON document on SQLite | collect | 3.490 | 681.7 | 588.3 | 0.33 |

For SQLite, peak process memory remains about 83–84 MiB when streaming 100,000 and 1,000,000 rows, while the buffered million-row run reaches about 697 MiB. Caller batches of 1000 rows stay around 86 MiB. Collecting the stream restores the large retained result, as expected.

For the continuous JSON document, streaming stays around 82 MiB instead of about 2131 MiB buffered. This path allocates more bytes in total, about 4.17 GiB versus 3.35 GiB, even though fewer bytes remain live at once. It is effective at reducing peak memory, and it still has allocation costs worth profiling.

Per-row JSON paths allocate about 1.24 GiB in total when streaming the million rows, versus 1.26 GiB buffered. The primary improvement is object lifetime, early access, and reduced GC pressure; this experiment does not eliminate row and JSON string allocations.

## DuckDB findings

The first implementation iterated a reader but left DuckDB.NET in its default materialized mode. At one million rows, its streaming process reached about 778 MiB. Enabling `UseStreamingMode` reduced the final ordered-query median to about 499 MiB, versus about 1224 MiB buffered.

Removing `ORDER BY` did not eliminate size-dependent native memory. The scan comparison measured about 197 MiB at 100,000 streamed rows and 503 MiB at 1,000,000 rows. Sampled managed peaks stayed around 8.4 MiB. Even stopping after the first object reached roughly 477 MiB in the million-row scan.

This rules out attributing the remaining growth solely to the explicit ordering clause. The provider or engine still performs size-dependent work before or during delivery. Its exact source was not diagnosed. A fixed end-to-end process-memory guarantee for DuckDB remains unproven.

The provider documents its separate [native streaming setting](https://duckdb.net/docs/basic-usage.html). The implementation requires that setting for DuckDB streams and leaves buffered commands unchanged.

## JSON document findings

The initial adapter returned after every text fragment. The final adapter coalesces fragments into a fixed 4096-character buffer before UTF-8 encoding and incremental deserialization. At one million rows, the initial document stream took about 3.32 seconds versus 2.80 seconds buffered. The final run measured about 2.65 seconds streaming versus 2.84 seconds buffered. These are separate runs with normal timing variation, so they do not isolate an exact optimization speedup.

The continuous-document workload is a real SQLite query that emits an array across database rows. It exercises `GetTextReader`, stateful transcoding, and the same incremental parser used for SQL Server. It does not measure SQL Server network traffic, native fragment sizes, or server execution.

Regression checks separately split JSON inside objects, escaped strings, raw Unicode surrogate pairs, and large tokens. They also prove that the first object arrives before the full document is read.

## Reproduce and inspect

See [benchmark commands and metric definitions](../../SqlLinqer.Streaming.Benchmarks/README.md) and [streaming usage and ownership](../RESULT_STREAMING.md).

- [Final 90 samples and medians](result-streaming-results.json).
- [Supplemental 30 DuckDB scan samples](result-streaming-duckdb-scan-results.json).
- [Initial 90 samples](result-streaming-initial-results.json).
- [Interactive comparison](result-streaming.html).

Setup runs outside measurement workers. Each worker warms both loading paths with 128 rows, forces GC before timing, and validates the consumed row count, ID sum, and text lengths. Modes run sequentially in a deterministic shuffled order. Process peaks include native engine memory, runtime overhead, warmup, and caches. Managed peaks are sampled every 5 milliseconds and can miss brief spikes. Post-GC retained managed bytes also include caches.

Hardware: AMD Ryzen 5 7640U, 12 logical processors, about 60 GiB RAM, Linux x64. Runtime: .NET 10.0.13. DuckDB uses two query threads and a 1 GiB engine memory limit. The OS file cache is not flushed between workers. Timing results describe this machine and dataset.

- DuckDB ordered provider 1.5.6.1, engine v1.5.6.
- SQLite provider 10.0.13.0, engine 3.53.3.

## Validation and remaining limits

The conversion follow-up passes all six container-backed suites and 2629 checks: regression 1693, SQLite 306, DuckDB 159, SQL Server 45 local plus 126 live, PostgreSQL/MySQL integration 252, and scale 48. The scale suite uses 10,000 rows per embedded provider and three samples. Runner validation passes another 34 checks. Compatibility projects build for .NET 10 and .NET Framework 4.8 with zero warnings and errors.

See the [conversion validation summary](result-conversions-full-suite.json), [complete passing output](result-conversions-full-suite.txt), and [conversion contract](../RESULT_CONVERSIONS.md).
The earlier 1177-check streaming run remains in the [original summary](result-streaming-full-suite.json) and [original output](result-streaming-full-suite.txt).
The large-result performance measurements above predate the conversion follow-up and were not repeated in that batch.

Shared real-provider tests run `Execute`, `ExecuteAsync`, and `ExecuteAsyncStream` for nested SQL mappings, binary and nullable values, API attribute isolation, paging, projections, aggregates, captured commands, and large Unicode JSON. Streaming cases additionally check early disposal on the caller connection, rollback, cancellation, and provider errors.

The full run exposed and verified fixes for PostgreSQL/MySQL binary JSON transport and SQL Server empty transport rows. Binary values now use explicit Base64 encoding, with encoder line breaks removed. SQL Server empty streams match buffered empty results. The binary test fixture explicitly declares its parameter type for nullable SQL Server binary values.

The streaming tests cover deferred execution, captured builders, repeated enumeration, sequential access, native DuckDB streaming mode, nested mappings, API JSON attribute isolation, binary values, projections, cancellation, consumer exceptions, failure stages, disposal, caller rollback, and DuckDB deadlines across consumer pauses.

The original performance runs could not access Docker from the session's stale groups. A child shell refreshed the account's existing Docker membership and ran the normal test runner without changing system permissions. All disposable containers were removed after the passing run.

SQL Server document parsing now has live functional coverage, including large values and empty results. Live SQL Server, PostgreSQL, and MySQL performance and memory remain unmeasured. These functional test results do not extend the earlier performance measurements to remote databases.

This experiment provides async select and projection streams. Synchronous streaming, callable result sets, write outputs, automatic batching helpers, and strict byte budgets remain outside its scope. A root object still materializes its nested collections and large values.
