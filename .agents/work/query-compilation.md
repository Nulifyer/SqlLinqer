# Query compilation and async execution

Status: complete
Repository: /home/nulifyer/Projects/SqlLinqer
Base: 4a0a3ba plus existing uncommitted connector and SQL Server work

## Goal
Preserve the fluent and synchronous interfaces while isolating compilation state, separating select preparation and dialect rendering, describing result shapes, and adding real asynchronous execution.

## Acceptance checks
- [x] Captured queries retain their structure, model names, and supported mutable parameter values after builder changes.
- [x] Repeated and concurrent compilation does not mutate builders or reuse parameter collections.
- [x] Data and count preparation preserve DISTINCT, grouping, child pagination, and relationship semantics.
- [x] Database compilers own select assembly, pagination, and nested JSON syntax.
- [x] Result metadata and shared JSON mapping separate result shape from transport.
- [x] Sync and async execution cover selects, counts, writes, output/primary-key results, TVPs, stored procedures, and typed callables.
- [x] Cancellation propagates, resources are disposed, and caller transactions remain caller-owned.
- [x] Existing local suites, new regression/provider checks, and demo builds pass.
- [x] Documentation describes the interfaces and relevant compatibility limits.

## Scope
In: compilation, result mapping, connectors, executable interfaces and query types, focused tests, documentation.
Out: full LINQ translation, schema generation, compilation caches, public immutable fluent builders, streaming, publication.

## Current state
Phase: locally verified implementation complete; local Git delivery authorized
Next action: deliver the verified work as local commits. Optional live server validation requires Docker access or configured disposable endpoints.

## Decisions
| Decision | Reason | Evidence |
| --- | --- | --- |
| Keep the public mutable fluent interface and both execution styles. | User approved the design and requested implementation. | Conversation |
| Freeze at compilation entry; retain an explicit reusable capture interface. | Existing Table/Column aliases and nested query nodes are mutable. | Compilation/QuerySnapshot.cs; Compilation/SnapshotGraph.cs |
| Cache runtime field layouts and isolate the existing graph before considering persistent immutable nodes. | Preserves current public node classes and virtual implementations without copying on each fluent call. | Compilation/SnapshotGraph.cs; docs/QUERY_COMPILATION.md |
| Add sibling asynchronous interfaces. | Existing synchronous interface implementers need no new members. | Queries/Interfaces/IAsyncQueryExecutable*.cs |
| Preserve ordinary legacy procedure TVP reference behavior; copy values in explicit captures and async execution. | Existing provider compatibility checks require ordinary rendering to retain the input table. | Queries/Core/Abstract/BaseQuery.cs; Queries/Typed/StoredProcedureQuery.Async.cs |
| Use fresh compilation state and bindings for each render. | Count and normal rendering currently stash and restore shared collections. | Queries/Core/SelectQuery.cs |
| Preserve pre-existing changes. | Workspace contains connector binding and ongoing SQL Server suite work. | Baseline patch and hashes in /tmp/sqllinqer-implementation-baseline |

## Findings
| Finding | Evidence | Status |
| --- | --- | --- |
| Baseline default test command passed four local suites. | /tmp/sqllinqer-baseline-tests.log | Confirmed |
| Test suite names/files are being changed concurrently from AzureSql to SqlServer. | SqlLinqer.Tests/Program.cs | Re-read before editing shared test registration |
| Library targets netstandard2.0, so transaction begin/commit/disposal have no async base contracts. | SqlLinqer/SQLLinqer.csproj | Preserved; async provider I/O with synchronous transaction lifecycle |
| Delivery run passed regression 574, SQLite 101, DuckDB 56, and offline SQL Server 45 checks, including aggregate count decisions after paging changes during I/O. | /tmp/sqllinqer-commit-tests.log | Confirmed, 776 checks across four passing suites |
| Both renamed compatibility projects build after the final library changes. | /tmp/sqllinqer-commit-net10-build.log; /tmp/sqllinqer-commit-netframework48-build.log | Confirmed, zero warnings and errors |
| Snapshot allocation benchmark completed for 10, 100, and 1000 predicates. Capture allocated about 20 KB, 37 KB, and 237 KB respectively. | /tmp/sqllinqer-final-render.log | Allocation measurements only, not throughput claims |
| Rendering allocations increased from baseline: 31,120 to 50,128 bytes at 10 predicates, and 2,390,168 to about 2,616,000 bytes at 1000 predicates. | /tmp/sqllinqer-baseline-render.log; /tmp/sqllinqer-final-render.log | Graph isolation has a measured allocation cost |

## Changes
| Path or artifact | Change | Verification |
| --- | --- | --- |
| SqlLinqer/Compilation | Snapshot ownership, field-plan adapter, select preparation, dialect compilers, and immutable logical result metadata. | Mutation, repeated-count, concurrent rendering, and provider checks |
| SqlLinqer/Connections and Components/Json | Async execution/cancellation, shared JSON mapping, schema-preserving async DataTable loading, and scoped DuckDB interruption. | Failure-stage, token, ownership, schema, and real embedded database checks |
| SqlLinqer/Queries | Async typed/aggregate/write/output/TVP/procedure/callable methods and sibling contracts; direct write rendering also isolates state. | Regression and both demo builds |
| SqlLinqer/Components/Insert | Captures mapped entity values before batching and async suspension. | Entity mutation and multi-batch regression cases |
| SqlLinqer.Tests | Snapshot and async regression cases, native provider checks in their own suites, opt-in external integration cases, allocation measurements. | Default test executable passed; external endpoints unavailable |
| docs/EXECUTION.md; docs/QUERY_COMPILATION.md; README links | Usage, ownership, copying cost, provider limitations, and custom connector hooks. | Links checked; examples follow compiled interfaces |
| .agents/work/query-compilation.md | Implementation ledger included in the authorized delivery. | Final evidence recorded |

## Risks and unknowns
- Live PostgreSQL, MySQL, and SQL Server execution was not run in this task. Docker denied socket access, and non-interactive sudo required a password. The existing integration suite now includes async PostgreSQL/MySQL cases for future runs.
- Earlier workspace documentation reported a SQL Server TVP error 1205; its current live behavior was not re-established here.
- Snapshots copy a graph for capture and for each render. Field layouts are cached, but capture is not constant-time, and measured allocations increased. No compilation cache or persistent immutable graph was added.
- Provider-specific scalar objects and extension-owned reference data must remain immutable. Builders must not be mutated during capture.
- Async provider contracts can complete synchronously in embedded providers. The netstandard2.0 transaction lifecycle remains synchronous.
- The user authorized committing all pending changes. The delivery includes connector binding, compilation and async execution, compatibility project renames, and the existing review-document removal. The SQL Server suite migration and container runner were committed separately before this delivery.
