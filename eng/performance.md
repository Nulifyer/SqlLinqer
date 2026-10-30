# Performance work

The supported pipeline reads native provider values into complete roots. Server providers and DuckDB use a correlated graph query; SQLite uses prepared child queries on the root connection. There is no automatic JSON transport fallback or core System.Text.Json dependency. The .NET Standard 2.1 asset has no package dependencies; 2.0 retains async-interface compatibility.

Compilation preserves detached mutable inputs, shared references, cycles, and per-compilation table aliases. Reference indexes belong to a compilation, not a global execution cache. Snapshot copy plans cache type metadata without retaining application values. DISTINCT runs in SQL on each node's scalar projection and required relationship inputs. No structural graph keys or captured-node stores remain. Parent DISTINCT does not deduplicate child collections.

## Reproduce before optimizing

Run [provider correctness checks](build-and-test.md) before timing. Use the maintained [embedded benchmark](streaming-benchmarks.md), [SQL Server benchmark](sqlserver-benchmarks.md), or [profiling tools](profiling.md). Save results under ignored `TestResults/`, outside source directories. No builds, tests, or other profilers should run alongside timing samples.

Use at least seven alternating process samples, identical fixtures/providers, explicit cold/warm phases, and full graph checks. Preserve outliers. Report compile time, first complete root, full consumption, cancellation/disposal, cumulative allocation, retained/peak memory, sent/received provider bytes, and actual database plans separately. Allocation counters are not peak RAM, and provider byte counters are not TCP captures. Identify binaries/source versions instead of comparing similarly named reports.

Managed EventPipe samples can be biased toward GC safe points and include waits. Cross-check CPU rankings with own-process kernel samples where available. A client flame graph does not measure CPU inside SQL Server. SQL Server plan evidence does not establish nested first-root latency or avoidance of expanded sorts on PostgreSQL, MySQL, or DuckDB; collect provider-specific nested plans and latency samples before making that claim.

## Remaining targets

- Reduce per-source connected-graph discovery copies and graph count preparation while preserving isolation, cycles, aliases, and captured values.
- Measure typed scalar/default-constructor staging and collection construction before changing ownership or adding pools.
- Investigate database completion/framing spools independently of client parsing.
- Measure SQLite command preparation, parameter updates, and deep/sparse graphs separately from root ordering.
- Measure schema-bound scalar reads before specializing default-constructor staging; preserve the provider conversion matrix and constructor behavior.

Do not add FastMapper as a dependency: the inspected implementation's cache identities and member index size did not fit this library. Cached schema-bound operations and accessors are useful design principles; they do not require another mapper or a second model parser.
