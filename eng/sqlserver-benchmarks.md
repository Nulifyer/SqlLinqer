# Production native graph versus framework JSON

This benchmark compares the current native library pipeline with an ADO.NET + System.Text.Json control on SQL Server 2022.
JSON exists only in this benchmark control, not in core execution.
The project references the test executable to reuse its pinned SqlClient package dependency; nothing here is packaged with core.

Run from the repository root with Docker and the .NET 10 SDK:

```sh
python3 SqlLinqer.Streaming.Benchmarks/SqlServer/run.py
```

The runner uses the official .NET 10.0.13 runtime at `TestResults/sqlserver-official-runtime`, matching the earlier SQL Server experiments. To prepare that cache from the official image:

```sh
task_runtime_container=$(docker create mcr.microsoft.com/dotnet/runtime:10.0.13)
mkdir -p TestResults/sqlserver-official-runtime
docker cp "$task_runtime_container:/usr/share/dotnet/." TestResults/sqlserver-official-runtime/
docker rm "$task_runtime_container"
```

The script builds Release before measurements and creates a disposable SQL Server container with generated credentials. It removes the container and anonymous volumes in `finally`. The C# worker creates and drops its own database.

The fixture has 2,000 roots with a clustered root key, indexed child foreign keys/order keys, two orders with two items each, duplicate tags, and present profiles with NULL notes.
Warmup checks every selected scalar and nested value against explicit expected values.
Each measured run validates root count, root-key checksum, nested cardinalities, and profile presence.

There are seven alternating samples for each transport and each consumption mode. Connections and transactions open before timing and before resetting byte statistics. Full streaming consumes all roots without retaining them. Early stop reads one complete root and explicitly cancels unfinished work for both transports before reader disposal. Timings include common validation and cleanup; SQL compilation and connection establishment are excluded.

`providerBytesReceived` and `providerBytesSent` are SqlClient counters, not a TCP/TLS packet capture. They measure provider traffic and include provider read-ahead. Prior encrypted-TCP studies remain separate evidence. `sqlCharacters` records command text length separately.
Managed allocation uses the process-wide allocation counter, including asynchronous continuations.
Actual execution plans are captured separately from timed samples.

Results are saved to `TestResults/sqlserver-native-vs-json.json`. The control's fragment stream was copied from core commit `0b7d946`; System.Text.Json performs parsing and model construction using these public DTOs. This is a framework JSON control, not a claim that every detail of the retired custom SQL mapper is identical.

To compare a frozen, binary-compatible core assembly with the current implementation:

```sh
python3 SqlLinqer.Streaming.Benchmarks/SqlServer/run.py \
  --baseline-core TestResults/my-frozen-baseline/SQLLinqer.dll \
  --output TestResults/sqlserver-core-comparison.json
```

This mode runs seven alternating isolated processes per version, with the same benchmark executable and dependencies. Each process creates its own identical disposable database, warms both transports and modes, then records one sample per transport/mode. It records assembly hashes and the first pair of actual plans. Build and test activity must finish before timing. This separates core changes from unrelated changes to the framework JSON control.

The harness also supports the published 2.3.1 core. It binds that core's public `ExecuteReaderJsonAsyncStream` once through a benchmark-only delegate; the current core binds `ExecuteReaderModelAsyncStream`. Released samples are labelled `released-library-json`, separately from `framework-json-control`. Supply its declared `Microsoft.Bcl.AsyncInterfaces.dll` beside the baseline DLL; for .NET 10 use the package's netstandard2.1 forwarding asset. The runner includes that support assembly in the frozen baseline dependency metadata. Never copy this version adapter into production execution.

## Large nested datasets

The stress runner compares the actual released library with current native execution. Its model is `User → Groups → Permissions → Grants`. Fixtures vary root count, collection size, nesting, and string width. The largest fixture contains one user with more than one million descendants.

```sh
python3 SqlLinqer.Streaming.Benchmarks/SqlServer/stress.py \
  --baseline-core TestResults/released/SQLLinqer.dll \
  --runs 7 --output TestResults/nested-stress/results.json
```

Place the released core's async-interface support DLL beside it as described above. Choose a fresh output path. Docker and .NET 10 are required. The runner copies the runtime from the official cached `mcr.microsoft.com/dotnet/runtime:10.0` image and runs both versions beside a disposable SQL Server 2022 instance. The raw results record runtime versions, assembly hashes, and the server image identity.

Each sample uses a new worker process. Seven alternating samples per version cover full streaming, buffered execution, and stopping after the first complete user. Both versions use the same fixture and validate every selected scalar, relationship, collection position, NULL, root count, and expected object count. Full streams discard completed users. Buffered execution retains the returned list through the final memory measurement.

Compilation, connection establishment, transactions, and three small warmups occur before measurement. Tiered compilation is disabled for both versions. Elapsed time includes result validation and stream cleanup. First-user latency excludes cleanup, which is included in total elapsed time. Buffered execution exposes only its completed result, so its first-user latency is recorded as null.

Managed allocation uses `GC.GetTotalAllocatedBytes`. A separate host sampler reads the worker's resident memory every 5 ms. Results include resident memory before measurement, sampled peak resident memory, and managed memory after a forced collection. Resident memory includes the runtime and native provider memory. It is sampled, so brief peaks can be missed. Retained managed memory includes surviving runtime and library state, not just returned models. The server has a 2 GB SQL memory setting and a 4 GB container limit; these are controlled test conditions, not production sizing advice.

Use `--cases`, `--modes`, and `--timeout` for focused runs. Use `--trace --runs 1 --modes stream` in a separate output directory to collect allocation traces. Instrumented samples are marked and must not be mixed with timing evidence. Generated fixtures, worker copies, reports, and traces stay in ignored `TestResults/` directories.

Use `--distinct` to measure root graph DISTINCT separately. Use `--nested-distinct` to add a small distinct group projection beside the full nested branch. The benchmark configures that internal projection before timing in both versions and validates its values separately. Use `--baseline-label before --candidate-label after` when both DLLs use native transport. The raw samples identify their transport and assembly hashes. Capture actual plans in a separate run with `--plans-only`; plan collection does not produce timing samples.
