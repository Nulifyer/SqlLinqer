# Layer profiles and flame graphs

These tools are benchmark-only. They add no dependencies or instrumentation to the core package.

Install the pinned .NET tool in this worktree:

```sh
dotnet tool install dotnet-trace --version 10.0.750501 --tool-path TestResults/profiling/tools
python3 SqlLinqer.Streaming.Benchmarks/Profiling/run.py \
  --output TestResults/profiling/my-managed-capture
python3 SqlLinqer.Streaming.Benchmarks/SqlServer/run.py \
  --profile-directory TestResults/profiling/my-managed-capture
```

The first command builds Release, then runs separate 12-second captures for cold model construction, graph capture, native graph rendering, and native graph mapping. `--layers`, `--seconds`, and `--allocations` select workloads/duration or add sampled allocation/GC events to the original trace. The target uses runtime 10.0.13 for continuity with previous measurements. The SQL Server runner uses the existing disposable Docker fixture and cached official runtime described in its README.

Workers prepare fixtures and warm for two seconds before attachment. A READY/go gate starts work after tracing attaches. A completion event stops collection; the worker stays alive until the collector flushes its rundown data. Each capture retains `.nettrace`, `.speedscope.json`, workload metadata, and whole-process self/inclusive reports. Raw artifacts stay under ignored `TestResults/`; choose fresh directories to avoid replacing evidence.

The mapping fixture uses 2,000 roots from the existing regression records, including duplicate children, parent-last delivery, and a present NULL-note object. It exercises the real native mapper with a DataTable reader. It excludes network/database execution and includes that reader's costs. The actual SQL Server capture repeatedly consumes 2,000 indexed roots through the async native pipeline, with connections, transactions, and compilation prepared before recording.

## CPU cross-check on Linux

`dotnet-trace collect` samples managed thread time. Its stacks can include waits and suffer GC safe-point bias. The current Microsoft-recommended Linux collector, `collect-linux`, combines kernel sampling with managed events but requires root. See [Microsoft's profiling guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-highcpu) and [dotnet-trace reference](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).

On this machine, own-process user-space `perf` sampling works without root or changes to system settings:

```sh
# Build before recording; perf_capture.py uses the existing Release binaries.
dotnet build SqlLinqer.Tests -c Release
python3 SqlLinqer.Streaming.Benchmarks/Profiling/perf_capture.py \
  --perf /usr/bin/perf --layers model capture render map \
  --output TestResults/profiling/my-cpu-capture
```

Omit `--perf` to use the task-local binary at `TestResults/profiling/perf/usr/bin/perf`. The recorded run used perf 7.2.11-1 and its missing libraries extracted into that directory from the machine's configured package mirror; nothing was installed globally. Package URLs/hashes are preserved in the profiling manifest. On another machine, use its compatible installed perf. If perf permissions prevent collection, report that limitation rather than modifying system settings automatically.

CPU capture uses `cpu-clock:u`, 199 Hz, frame-pointer call chains, and `DOTNET_PerfMapEnabled=1`. It samples only the worker process and its threads, not SQL Server or the whole machine. The analyzer resolves JIT addresses against the copied runtime perf map, checks address ranges, and keeps unresolved native frames explicit. Optimized inlined methods can be absent. These captures do not resolve every runtime native symbol.

## Analyze and view

```sh
python3 SqlLinqer.Streaming.Benchmarks/Profiling/test_analysis.py
python3 SqlLinqer.Streaming.Benchmarks/Profiling/analyze.py \
  --thread-time TestResults/profiling/my-managed-capture \
  --cpu TestResults/profiling/my-cpu-capture \
  --output TestResults/profiling/my-summary.json
python3 SqlLinqer.Streaming.Benchmarks/Profiling/render.py \
  TestResults/profiling/my-summary.json TestResults/profiling/my-flamegraphs.html
```

Open the standalone HTML locally. Select a workload, click frames to zoom, search to highlight, and hover for full names and inclusive shares. The original Speedscope files also open in [Speedscope](https://www.speedscope.app), or inspect `.nettrace` with PerfView/Visual Studio. No trace upload is required by this workflow.

The summary selects library/provider call paths plus the isolated model/mapping workload entry points where optimized inlining can hide core frames. Framework/provider callees remain included. Idle/background paths outside those stacks are excluded from this selected denominator. Whole-process reports and original data remain available. Inclusive recursion counts each method once per stack; inclusive percentages overlap. Layer groups attribute each selected stack to its deepest recognized owner. They are call-path categories, not precise subsystem accounting. Perf counts are on-CPU samples; EventPipe weights are sampled thread-time intervals. Neither is a method invocation count, allocation count, or benchmark result.

Use actual database execution plans, statistics, and provider traffic counters alongside the client profile. A client flame graph cannot attribute CPU inside a separate database process. Benchmark any resulting optimization with uninstrumented repeated runs.
