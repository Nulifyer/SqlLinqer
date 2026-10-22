# Result streaming experiment

Status: complete
Repository: /home/nulifyer/Projects/SqlLinqer
Base: master at 861cd6d; branch feature/result-streaming

## Goal
Implement experimental connector and typed query streaming. Measure actual performance and memory usage with large result sets.
Run the full container-backed suite with shared checks for Execute, ExecuteAsync, and ExecuteAsyncStream.
Define result conversion rules and test numeric, date/time, string, and nullable conversions across JSON and native read paths.

## Acceptance checks
- [x] Async streams preserve SQL model mapping, query capture, and repeatable enumeration.
- [x] Early exit, failures, cancellation, and caller transactions have verified resource ownership.
- [x] SQL Server JSON fragment parsing handles arbitrary boundaries and Unicode without a complete document buffer.
- [x] Real SQLite and DuckDB large-result measurements compare buffered, streaming, and caller batching in isolated processes.
- [x] Reproducible commands, results, limitations, and compatibility checks are documented.
- [x] Shared real-provider checks exercise sync, async, and async-stream execution, including large values and binary values.
- [x] All six suites pass through the container runner with live SQL Server, PostgreSQL, MySQL, and scale checks enabled.
- [x] Generated conversion cases cover numeric widths, dates, offsets, strings, nullable values, and expected errors across JSON and native read modes.
- [x] Real-provider conversion fixtures, a second timezone, runner validation, and both consumer compatibility builds pass.

## Scope
In: async connector JSON streams, typed selects and projections, shared conversion rules and matrices, three-mode provider and scale checks, the full container-backed suite, independent benchmark project, documentation.
Out: synchronous streaming, callable sets, write outputs, pushes, and pull requests. The user subsequently authorized local integration and commits.

## Current state
Phase: streaming, conversions, tabular mapping, provider test refactors, and scale checks are consolidated on local master.
Next action: no implementation work remains for local integration. Remote publication requires a separate user request.
Current behavior is documented in docs/execution/value-conversions.md. Historical experiment evidence remains in docs/experiments/result-conversions-full-suite.json and its linked logs.
Local integration validation passed all 2629 checks across six suites, including live SQL Server, PostgreSQL, MySQL, and scale checks.
It also passed 34 runner checks, 306 checks against SQLite 3.38.0, both compatibility builds, and 20 streaming worker smoke checks.
Current logs are /tmp/sqllinqer-master-verification-9hm5l123/full-suite-final.log and the other logs in that directory.
The full-suite runner also saved /tmp/sqllinqer-tests.YevvSs4R.log. Live Azure SQL was not run because no endpoint was configured.
Minimum-engine validation exposed a quoted-column fallback in the streaming error fixture. Qualifying the missing column restores the expected provider error.

## Decisions
| Decision | Reason | Evidence |
| --- | --- | --- |
| Preserve existing buffered execution and add IAsyncQueryExecutableStream. | Existing response and interception contracts describe completed operations. | Connections/BaseConnector.Streaming.cs |
| Keep the operation name and use AsyncStream as the streaming suffix. | User requested consistent Execute, ExecuteAsync, and ExecuteAsyncStream naming across methods. | Queries/Interfaces/IAsyncQueryExecutableStream.cs; Connections/BaseConnector.Streaming.cs |
| Decode per-row JSON directly; stream SQL Server fragments through the framework parser. | Avoid a new parser and avoid extra per-row adapters where a full root value already exists. | Components/Json/JsonResultMapper.cs; JsonFragmentStream.cs |
| Keep benchmark setup outside measurement worker processes. | Fixture creation and previous buffered runs otherwise contaminate peak memory. | Planned benchmark runner |
| Preserve pre-existing test work. | Initial worktree contains scale-suite and command-runner changes. | Baseline patch /tmp/sqllinqer-streaming-baseline/tracked.patch |

## Findings
| Finding | Evidence | Status |
| --- | --- | --- |
| The library needs an explicit Microsoft.Bcl.AsyncInterfaces reference when publishing async stream contracts. | SqlLinqer/SQLLinqer.csproj; initial consumer assembly-resolution failure | Fixed; both compatibility projects build |
| The account belongs to the Docker group, but the original session has stale supplemental groups. A newgrp docker child shell can access Docker 29.7.2 and the test images. | id nulifyer; newgrp docker running docker info and docker image ls | Full container suite is now available without system permission changes |
| The conversion follow-up passed regression 1693, SQLite 306, DuckDB 159, SQL Server 45 local plus 126 live, integration 252, and scale 48 checks. | docs/experiments/result-conversions-full-suite.json; result-conversions-full-suite.txt | Confirmed; 2629 checks, all six suites pass, no skips |
| The generated matrix executed 55,678 cases, with 462 additional real-provider fixture groups. Runner validation passed another 34 checks; both compatibility builds report zero warnings and errors. Regression 1693 and DuckDB 159 checks also pass under Pacific/Auckland. | docs/experiments/result-conversions-full-suite.json; result-conversions-validation.txt | Confirmed after conversion fixes |
| Final and supplemental runs contain 120 validated samples; the initial experiment retains another 90. | docs/experiments/result-streaming*-results.json | Confirmed; three isolated processes per mode and size |
| At one million rows, SQLite streaming uses 84.1 MiB peak process memory versus 696.7 MiB buffered; elapsed time is 1.934 versus 2.926 seconds. | docs/experiments/result-streaming-results.json | Median of three samples |
| The continuous JSON document uses 82.5 MiB streaming versus 2130.5 MiB buffered, but streaming allocates more total bytes. | docs/experiments/result-streaming-results.json | Real SQLite transport experiment, not SQL Server performance |
| DuckDB needs UseStreamingMode enabled, and total native memory still grows even without ORDER BY. | docs/experiments/result-streaming-duckdb-scan-results.json; report | Measured limitation; exact native cause unknown |
| At the end of the initial experiment, the four pre-existing tracked test diffs matched their baseline sections. | /tmp/sqllinqer-streaming-baseline/tracked.patch; initial comparison | Historical preservation check; the user subsequently authorized extending scale tests and their documentation |
| The full run exposed binary JSON decoding failures on PostgreSQL/MySQL and empty JSON transport failures on SQL Server streams. | Initial failing logs /tmp/sqllinqer-tests.w0KiRMvi.log and /tmp/sqllinqer-tests.ksrsyOvH.log; passing full-suite evidence | Fixed and verified across all three read modes |
| The initial tabular implementation appeared concurrently in the shared tree. The user subsequently authorized conversion fixes across native reads. | BaseConnector.Tabular.cs; TabularRegressionTests.cs; conversion matrix | Preserved the original contract tests and added direct reader mapping plus shared conversion rules |
| A DataTable intermediate removed DateTime.Kind from tabular model lists. | ValueConversionRegressionTests.cs; initial failing conversion logs | Fixed by mapping provider reader values directly into the buffered list |
| DuckDB.NET returns DATE as DateOnly and TIMESTAMPTZ as an unspecified DateTime holding a UTC clock. Its JSON uses the connection timezone. | /tmp/sqllinqer-conversion-provider-probe.log; real provider conversion fixtures | Added native type adapters and SQL metadata-based UTC restoration |

## Changes
| Path or artifact | Change | Verification |
| --- | --- | --- |
| SqlLinqer/Connections/BaseConnector.Streaming.cs | Deferred execution, disposal, errors, interception, and native DuckDB streaming. | Regression and real embedded-provider checks |
| SqlLinqer/Components/Json | Fixed-buffer fragment coalescing, incremental document decoding, and per-root streaming. | Unicode, large token, malformed document, early yield, and benchmark checks |
| SqlLinqer/Queries | Captured typed select and projection streams. | Snapshot, repeat enumeration, attribute isolation, binary, nesting, and projection checks |
| SqlLinqer.Tests/Regression/StreamingRegressionTests.cs | Stream semantics and resource tests, registered in the existing regression suite. | Final local run |
| SqlLinqer.Tests/Regression/SqlJsonContractChecks.cs; SqlLinqer.Tests/Scale | Shared sync/async/async-stream matrix, binary/large values, projections, empty results, streaming lifecycle, and full-scale row validation. | Complete container run |
| SqlLinqer/Components/Select/SelectColumn.cs | Normalize PostgreSQL/MySQL binary JSON to Base64 without line breaks. | 1024-byte and NULL values in all three modes against real providers |
| SqlLinqer/Components/Json | Accept zero SQL Server JSON transport rows as an empty stream. | SQL Server/Azure fake regressions and live SQL Server empty-result checks |
| SqlLinqer/Connections/DbValueConversion.cs; Components/Modeling | Shared native conversion rules, checked integers, explicit date parsing and formatting, modern provider date/time adapters. | Generated scalar/tabular conversion matrix and real-provider fixtures |
| SqlLinqer/Connections/BaseConnector.Tabular.cs; BaseConnector.Execution.cs | Preserve DateTime.Kind during model mapping and restore DuckDB TIMESTAMPTZ UTC metadata for scalar/tabular conversions. | Mixed-kind rows, date/string round trips, all real providers, and second-timezone checks |
| SqlLinqer/Components/Json | Share native date parsing and reject nonfinite floating-point conversions. | All three JSON modes, all dialects, nullable and invalid input cases |
| SqlLinqer.Tests/Regression/ValueConversion*; docs/execution/value-conversions.md | Generated conversion matrix, native SQL fixtures, explicit behavior and compatibility changes. | 2629 full-suite checks and saved conversion evidence |
| SqlLinqer.Tests/SqlServer | Correct fixture-stage attribution and report nested exception types/numbers without provider messages. | Passing live SQL Server suite |
| SqlLinqer.Streaming.Benchmarks | Independent Release benchmark and isolated-process runner. | 210 validated measurement processes across initial, final, and supplemental phases |
| docs/execution/result-streaming.md; docs/experiments | Usage, measured report, raw data, and interactive comparison. | Link checks and desktop/mobile HTML previews |

## Risks and unknowns
- DuckDB native memory remains size-dependent even with UseStreamingMode and without ORDER BY. Managed retention stays small; end-to-end bounded process memory is unproven.
- Live SQL Server, PostgreSQL, and MySQL functional checks pass. Their performance and memory were not benchmarked. Live Azure SQL remains optional and was not run.
- Continuous-document streaming allocates more total managed bytes than buffered decoding in this workload despite much lower peak memory.
- Streaming materializes each complete root and nested collections. Batch size controls row count rather than a strict byte budget.
- The user authorized consolidating pending work and local branch histories into master. Existing worktrees remain available.
- The file-splitting refactor is included. Connector conversion edits live in BaseConnector.Execution.cs and BaseConnector.Tabular.cs.
- The large-result performance measurements predate this conversion batch and were not repeated. The latest scale suite still runs 10,000 rows per embedded provider.
- The repository uses legacy work ledgers rather than the canonical continuity file layout. The continuity validator reports the missing root AGENTS.md and .agents index/plan/status/testing/decisions files.
- The original preservation finding describes the experiment phase. The user later authorized extending the existing scale tests and test README for full-suite coverage; those files now include this task's additions.
