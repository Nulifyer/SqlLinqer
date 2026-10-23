# SqlLinqer.Diagnostics

Optional ILogger and ActivitySource instrumentation for SqlLinqer. Attach `SqlLinqerDiagnostics` through `ConnectorOptions.Observer` and use logger category `SqlLinqer`.

- Debug logs command lifecycle and completion.
- Trace logs opted-in SQL and typed parameter detail. Values require a separate opt-in.
- Error retains failed executions with underlying error classification.
- Optional Information summaries and provider-Execute slow warnings support operational monitoring.

SQL in logs, SQL in activities, values and full exceptions are independently controlled and disabled by default. Metadata, execution IDs and command roles support correlation without exposing values. Subscribe to ActivitySource `SqlLinqer` for tracing.

Available on nuget.org starting with `2.3.0-rc.2`. Use the matching core package version. See the [diagnostics guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/execution/diagnostics.md) for registration, runtime configuration, parameter limits and migration details.
