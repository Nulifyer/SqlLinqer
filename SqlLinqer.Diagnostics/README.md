# SqlLinqer.Diagnostics

Optional ILogger and ActivitySource instrumentation for SqlLinqer. Attach `SqlLinqerDiagnostics` through `ConnectorOptions.Observer` and use logger category `SqlLinqer`.

- Debug logs command lifecycle and completion.
- Trace logs opted-in SQL and typed parameter detail. Values require a separate opt-in.
- Error retains failed executions with underlying error classification.
- Optional Information summaries and provider-Execute slow warnings support operational monitoring.

SQL in logs, SQL in activities, values and full exceptions are independently controlled and disabled by default. Metadata, execution IDs and command roles support correlation without exposing values. Subscribe to ActivitySource `SqlLinqer` for tracing.

This implementation targets the next package release. The core release workflow now includes this optional package. Until it is published, use the repository project or a locally packed package. See the [diagnostics guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/execution/diagnostics.md) for registration, runtime configuration, parameter limits and migration details.
