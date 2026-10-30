# Diagnostics contract for NuGet consumers

## Configuration and ownership

Applications install core plus the optional diagnostics package and their database provider. They attach the observer through ConnectorOptions.Observer and supply a category-specific ILogger. Core remains independent of Microsoft logging, DI and OpenTelemetry SDKs. Applications own logger destinations, runtime thresholds and ActivitySource exporters.

## Event contract

- Debug: start and successful, cancelled or early-stopped completion.
- Trace: SQL text when IncludeSqlInLogs is enabled. Typed parameter metadata, with values only when IncludeParameterValues is enabled.
- Information: successful summaries only when LogSuccessfulQueriesAtInformation is enabled. Application workflow milestones belong to the application.
- Warning: optional SlowCommandThreshold, evaluated against provider Execute duration. It excludes stream consumer pauses and later reader iteration.
- Error: execution, mapping, timeout or cleanup failure. Include underlying error type, wrapper type, classification and safe provider code where available.
- Critical: the host decides whether service operation is impossible.
- None: no ILogger entries. Activity collection and inspector capture are separately controlled.

## Correlation and details

Every observed execution receives a stable execution ID. Compiler-created commands identify Select, Count, Insert, Update or Delete. Raw commands default to Command and can be labeled explicitly. Each logger event includes the execution ID, role and available trace/span IDs. Query names remain stable application tags.

IncludeSql remains a source-compatible alias for IncludeSqlInLogs. It no longer opts SQL into activities. IncludeSqlInActivities explicitly controls that destination. Parameter values never become activity tags. Exception details remain separately opt-in. Values use bounded invariant representations and distinguish NULL, empty strings and binary. Unknown objects must not run arbitrary ToString implementations.

A configuration-provider callback can return current options at operation start. The observer captures one consistent snapshot for that operation. Logger threshold reload follows the host. It cannot override a disabled detail permission.

## Consumer flows

1. First query: attach the observer, select the matching logger category, use a stable Tag, verify Debug start/completion.
2. Local SQL debugging: raise the category to Trace and opt into SQL logs. Inspect the statement and its separate typed bindings. Opt into values only when required.
3. Production baseline: application Information and query Warning, all raw-detail flags false. Retain Error and configured slow warnings.
4. Incident: correlate request/command IDs, classify failure without raw messages, then temporarily enable the required destination and detail controls.
5. Export: distinguish Cancelled and StoppedEarly from Failed. Interpret stream lifetime separately from Execute duration.
6. Inspector: enable independently for synthetic local demo data, disable/redact separately before using a similar flow with real data.

## Migration

Existing event IDs 1000 through 1003 remain. SQL/parameter events move from Debug to Trace. Routine completion moves from Information to Debug. Enable LogSuccessfulQueriesAtInformation to retain successful Information summaries. IncludeSql no longer adds activity SQL. Set IncludeSqlInActivities explicitly if that is intended. The MVC observer adopts the same severity rules and independent inspector controls.

Implementation validation covers each flow in both Debug and Release builds, actual logger filtering, cancellation/disposal, concurrent/page correlation, listener sampling, runtime option snapshots, formatter edge cases, and packed-package consumption. Documentation must distinguish release-ready source from packages that have actually been published.

## Implementation status

Core, diagnostics and DI packages are published at `2.3.0-rc.2`, with consumer configuration, matrix/policy tests, demo HTTP controls and NuGet-only validation. The original constructor remains available. Runtime invalid configuration falls back to redacted defaults with one configuration warning until recovery. The release workflow stages all three matching package versions. The publishing policy permits updates to those IDs; creation permission was enabled only for their first publication.
