# Result streaming benchmark

Run from the repository root:

```sh
python3 SqlLinqer.Streaming.Benchmarks/run.py \
  --rows 100000 1000000 --iterations 7 \
  --output TestResults/result-streaming-results.json
```

This builds Release once and creates temporary file-backed SQLite and DuckDB fixtures. Each measurement runs in a fresh process.
Setup is outside measurement processes, and each worker warms both buffered and streaming paths with 128 rows before measuring.
The runner interleaves modes in a deterministic shuffled order, runs workers sequentially, checks their exit status, and saves every raw sample after completion.
Temporary database files are removed when the runner exits.

Each row contains an integer ID, a 256-character string, and a nullable Unicode string. Change `--width` to vary the string size.
The worker validates row count, the sum of IDs, and total text length for every sample. All modes request the same data and ordering.

The modes are:

- `buffered`: existing `ExecuteAsync` loading a complete list before processing.
- `stream`: process each yielded object without retaining previous rows.
- `batch`: retain up to 1000 objects, process the batch, then clear it. Set `--batch-size` to change this.
- `collect`: retain every streamed object in a list. This checks the cost when a caller chooses to keep all rows.
- `first`: stop after one object and dispose the stream. This measures early termination, not full-result throughput.

Select subsets with `--providers` and `--modes`. Run one measurement directly after creating a fixture:

```sh
dotnet run -c Release --project SqlLinqer.Streaming.Benchmarks -- setup sqlite /tmp/streaming-example.db 100000 256
dotnet run -c Release --no-build --project SqlLinqer.Streaming.Benchmarks -- measure sqlite /tmp/streaming-example.db 100000 stream
```

The example creates a file that you own and can remove after the measurement.

## What the providers mean

`sqlite` and `duckdb` execute the actual typed native SELECT through those native providers.
DuckDB uses two query threads and a 1 GiB engine memory limit. An open keeper connection preserves these database settings for query connections.
The connector enables DuckDB.NET's native streaming result mode for streamed commands.

`duckdb-scan` runs the same DuckDB fixture without `ORDER BY`. It isolates result delivery from the ordered query's native working memory.
It is an optional supplemental comparison:

```sh
python3 SqlLinqer.Streaming.Benchmarks/run.py \
  --providers duckdb-scan --rows 100000 1000000 --iterations 7 \
  --output TestResults/result-streaming-duckdb-scan-results.json
```


## Metrics and limits

- Elapsed time includes query construction, compilation, connection opening, database execution, mapping, processing, and disposal.
- First-row latency measures when the caller can access the first object. Buffered execution exposes it after the whole result loads.
- Allocated bytes use process-wide `GC.GetTotalAllocatedBytes`, so async thread changes do not omit allocations.
- Peak managed memory samples `GC.GetTotalMemory(false)` every 5 milliseconds and checks it again after consumption. Brief peaks can be missed.
- Retained managed delta uses a forced collection after timing, with buffered or collected results kept alive. It includes managed caches and is not an exact object-size measurement.
- Peak working set comes from the operating system for the worker lifetime. It includes runtime, provider-native memory, warmup, and database caches.
- Collection counts are captured before the post-measurement forced collection.

GC preparation and result validation are outside the timed region, except for row counting and checksums, which every mode performs.
Timing is diagnostic and has no pass threshold. The runner does not flush the OS file cache or simulate a remote network.
Large sorts and provider-native buffers can affect process memory even when the connector retains no previous objects.
Do not compare `first` throughput with modes that consume all rows.

Use the [result streaming guide](../docs/execution/result-streaming.md) for API examples and transport limits.

## Metadata cache hits

Measure warm model-mapping lookups with default and explicit schemas:

```sh
DOTNET_TieredCompilation=0 dotnet run -c Release --project SqlLinqer.Streaming.Benchmarks -- cache
```

The command warms both keys before measurement and reports three allocation and timing samples for each.
It measures `Table.GetCached` through its public entry point. Cold reflection, expiration sweeps, and database work are excluded.
Regression checks separately verify sliding expiration, cleanup races, concurrent construction, and zero managed allocations on warm hits.
