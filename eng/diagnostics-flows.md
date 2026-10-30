[Documentation](../docs/README.md) · [Consumer configuration](../docs/execution/diagnostics.md)

# Diagnostics user flows and acceptance plan

The implementation supports these flows and supersedes the earlier characterization review. Core, diagnostics and DI packages are available on nuget.org at `2.3.0-rc.2`. The [consumer contract](diagnostics-implementation.md) records the design and migration decisions.

## LF1: Adopt logging and see the first query

A NuGet developer installs matching core/diagnostics versions and an ADO.NET provider, supplies a logger, and attaches the observer. The category in configuration matches the supplied logger. Debug shows start and completion without raw values. No observer means no library logs. A disabled logger with no activity listener skips observation.

Implemented: explicit observer registration, LoggerCategoryName, disabled fast path, repository minimal API and a NuGet-only consumer check using locally packed packages. The release workflow includes the optional diagnostics and DI packages.

## LF2: Debug a wrong SQL filter locally

The developer sets the query category to Trace and enables IncludeSqlInLogs. Each command logs its rendered statement with placeholders and an execution ID. Parameter metadata is typed. Values require another opt-in. A page's data and count commands remain distinguishable.

Implemented: Trace events 1002/1003, independent SQL/value permissions, stable execution IDs and compiler Select/Count roles. Existing IncludeSql remains a log-only alias. Statement text is not a parameter-expanded replay script.

## LF3: Keep production quiet while retaining failures

The operator keeps application Information logs and sets the query category to Warning. Failed queries still emit Error. The record contains underlying type, wrapper type, known failure classification and safe provider-code metadata without full messages. Benign cancellations and early disposal remain Debug.

Implemented: default Debug lifecycle, Error failure retention, optional successful Information summaries and opt-in full exceptions. The demo uses the same severity policy. Its production default disables the synthetic inspector.

## LF4: Investigate a slow screen or report

The operator correlates a request with execution IDs and available trace/span IDs, then inspects the relevant timing phase. Optional slow warnings identify provider Execute time. A paused export consumer does not trigger a slow-database warning.

Implemented: log/activity correlation, command roles, separate lifetime and Execute duration, optional positive SlowCommandThreshold and phase-labeled event 1004. Later reader iteration and mapping are not included in that warning measurement. A complete query-performance diagnosis still needs provider/database tools when those phases dominate.

## LF5: Reproduce parameter conversion problems

The developer first inspects parameter names, source CLR types and explicitly assigned DbTypes. If values are needed, they are enabled explicitly and formatted invariantly. SQL NULL, empty strings, binary and modern dates remain distinct. Large values are bounded with truncation and omission metadata. Unknown objects do not execute arbitrary ToString methods.

Implemented: structured DiagnosticParameterSet, independent value flag, configurable count/length limits, invariant formatting, typed null state and redacted metadata. These limits apply only to diagnostic output. They do not change query fields, filters or row counts.

## LF6: Cancel or stop an export

An observed stream starts at enumeration and completes after cleanup. Caller cancellation is Cancelled. Breaking enumeration is StoppedEarly. A consumer exception does not automatically become a database failure. Actual execution/mapping/disposal errors are Failed at Error.

Implemented: existing stream outcome and ownership behavior retained. New levels and correlation are covered by acceptance checks. Provider interruption remains provider-dependent and is separate from logging. Known timeout exceptions, including nested provider timeouts, receive Timeout classification.

## LF7: Follow parallel commands and page totals

Every execution has an identity even without an ActivitySource listener. Start, SQL, parameters and ending share it. A page and its total have distinct identities and roles. Parallel operations do not exchange IDs or settings snapshots.

Implemented: lazy thread-safe QueryExecutionContext.ExecutionId, RenderedQuery.CommandRole, compiler roles and explicit structured correlation fields. Specialized/raw commands can retain Command. A caller may label them without changing execution. The legacy public-RC demo generates a local execution ID and uses Command when new metadata is unavailable.

## LF8: Control destinations and runtime changes independently

The owner controls SQL logs, SQL activity tags, binding values, full exceptions and browser/API capture separately. Runtime logger thresholds cannot override detail permissions. Each started operation retains one settings snapshot. Invalid runtime settings preserve query results and failure logging while falling back to redacted defaults.

Implemented: independent flags, options-provider snapshots, one event 1005 per invalid-configuration period, and recovery on valid settings. The demo binds its own options and independently enables/redacts/disables inspector capture. Production profiles are empty by default. Activity sampling still controls whether SQL activity tags are populated. No binding values are activity tags.

## Level and migration contract

- Trace: opted-in SQL and typed bindings.
- Debug: lifecycle and ordinary completion/cancellation/early disposal.
- Information: explicitly enabled successful query summaries. Application workflow milestones remain application-owned.
- Warning: optional successful provider-Execute slow signal or invalid runtime diagnostics configuration.
- Error: failed query execution, mapping, timeout or cleanup.
- Critical: host-level service failure decisions.
- None: disable ILogger for the category. It does not disable other destinations.

Events 1000 through 1003 remain. SQL/value entries move from Debug to Trace. Routine endings move from Information to Debug. IncludeSql no longer enables activity SQL. Existing two-argument constructor calls remain binary-compatible. Consumers of flattened parameter text should move to structured fields. See the [configuration guide](../docs/execution/diagnostics.md) for examples.

## Verification and remaining limits

The integration suite exercises real logger filtering, SQLite execution, all levels/options, correlation, activities, sampling, concurrent/page commands, runtime changes, malformed runtime settings, failures, formatting and paused consumers. The suite runs in Debug and Release. The packed consumer uses NuGet references rather than project references.

The demo checks cover its logger thresholds, runtime options and inspector controls. Its HTTP checker launches private Production hosts to verify quiet success, retained failures, Trace SQL, redacted profiles and explicit synthetic detail.

Remaining boundaries: no automatic retries, service-health classification or user authorization policy is inferred by diagnostics. CommandDuration is not complete database time. Query construction/rendering are outside observer lifetime. Logger/provider formatting and host exception handling have independent behavior.
