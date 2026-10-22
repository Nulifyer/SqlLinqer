[Documentation](../README.md) · [Project README](../../README.md)

# Observe query execution

Name an operation with `.Tag("users.search")` and attach an observer through `ConnectorOptions.Observer`. Tags are diagnostic names; they do not change SQL or bindings. Use stable names without user input. A page with its total produces two observations with the same name: one for the page and one for its count.

## Structured logs and tracing

The optional `SqlLinqer.Diagnostics` project supplies `ILogger` logging and an `ActivitySource` named `SqlLinqer`. It is under development in this repository; reference the project to try it before publication. Core SqlLinqer does not depend on Microsoft logging or an OpenTelemetry SDK.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlLinqer;
using SqlLinqer.Diagnostics;

services.AddSqlLinqer(
    () => new SqlConnection(connectionString),
    DbFlavor.SqlServer,
    (options) => options.Observer = new SqlLinqerDiagnostics(logger));
```

Here `services`, `connectionString`, and `logger` belong to your application, and `SqlConnection` comes from `Microsoft.Data.SqlClient`. This example also references the optional DI project. To resolve the logger from DI, use the provider-aware overload:

```csharp
services.AddSqlLinqer(
    sp => new SqlConnection(connectionString),
    DbFlavor.SqlServer,
    (sp, options) => options.Observer = new SqlLinqerDiagnostics(
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("SqlLinqer")));
```

```csharp
var users = (await connector.BeginSelect<User>()
    .SelectRootColumns()
    .Tag("users.search")
    .ExecuteAsync(cancellationToken: cancellationToken))
    .GetResultOrThrow();
```

An `ActivityListener` can subscribe directly. If your application already uses OpenTelemetry tracing, add `"SqlLinqer"` to its tracer provider's sources with `.AddSource(SqlLinqerDiagnostics.ActivitySourceName)`. Configure exporters in your application. Activities use the current request activity as their parent.

Completion logs include the operation name, outcome, total duration, provider command duration, returned-row count when known, and error type. Successful and cancelled operations log at Information; failures log at Error. Start events log at Debug. Activities include the database system, streaming flag, outcome, timings, and known returned-row count. Failures set an error status.

SQL text, parameter values, and exception details are disabled by default. Enable each separately with `SqlLinqerDiagnosticsOptions` when needed. SQL and parameter logs require Debug logging. SQL text can also be attached to activities when `IncludeSql` is enabled; parameter values are never added as separate activity tags. Raw SQL text and provider exception messages can themselves contain sensitive values. Configure these options with your application's logging policy in mind.

## Understand the timing and row counts

Observation begins after query construction and rendering. Total duration covers connection acquisition, execution, mapping, and resource cleanup. It does not measure query construction or rendering.

Command duration measures the provider's `ExecuteReader`, `ExecuteScalar`, or `ExecuteNonQuery` call. It excludes later reader iteration and mapping. For providers that fetch results lazily, this is only the initial execute time, rather than the full time spent fetching data. It is absent if no provider command ran.

Returned rows count buffered collections or yielded models. Scalar results and affected-row counts have no returned-row count. A nested model counts as one root row, regardless of its children.

Streams start observation on enumeration. Total duration includes the consumer's time between yielded rows and ends after disposal. Each enumeration creates a new observation. Finishing enumeration records `Completed`; breaking early records `StoppedEarly`; requested cancellation records `Cancelled`; execution, mapping, or disposal failure records `Failed`. A consumer's own exception causes early disposal and is not reported as a database failure.

## Supply your own observer

Implement `SqlLinqer.Diagnostics.IQueryObserver` in an application that needs a different telemetry system. `Start(QueryExecutionContext)` returns an `IQueryObservation`, whose `Complete(QueryExecutionResult)` receives the outcome after resource cleanup. Return null from `Start` to skip observation. Treat the rendered query and its bindings as read-only.

Observers are shared with the connector and must support concurrent operations. Observer failures are ignored so instrumentation cannot replace a query's result or error. An observer that allocates resources must release them if its own `Start` fails and when `Complete` runs. Existing protected connector execution hooks remain available.
