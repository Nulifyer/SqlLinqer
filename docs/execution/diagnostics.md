[Documentation](../README.md) · [Project README](../../README.md)

# Configure query logs and tracing

Attach an observer to a connector and name operations with stable tags such as `.Tag("orders.search")`. Core SqlLinqer emits no ILogger messages unless you attach an observer.

This contract is implemented in the working source for the next release. The published core `2.3.0-rc.1` has observer interfaces but does not contain this diagnostics integration. Until the optional packages are released, use repository project references or locally packed packages. The release workflow now packages core, diagnostics and optional DI together.

## Install and register

Install matching released versions of `SqlLinqer` and `SqlLinqer.Diagnostics`, plus your ADO.NET provider. `SqlLinqer.Extensions.DependencyInjection` is optional. ASP.NET Core already supplies a logger factory and configuration.

A registration using ordinary Microsoft DI needs no SqlLinqer DI extension:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Diagnostics;

builder.Services.AddSingleton<BaseConnector>(services =>
    new ConnectionFactoryConnector(
        () => new SqliteConnection(connectionString),
        DbFlavor.Sqlite,
        new ConnectorOptions
        {
            Observer = new SqlLinqerDiagnostics(
                services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(SqlLinqerDiagnostics.LoggerCategoryName),
                options: null,
                optionsProvider: () => builder.Configuration
                    .GetSection("SqlLinqer:Diagnostics")
                    .Get<SqlLinqerDiagnosticsOptions>() ?? new())
        }));
```

Here `builder` is the application's `WebApplicationBuilder` and `connectionString` belongs to the application. The recommended category is `SqlLinqer`. A different supplied category needs a matching logging rule.

With the optional DI package, use its provider-aware overload:

```csharp
builder.Services.AddSqlLinqer(
    services => new SqliteConnection(connectionString),
    DbFlavor.Sqlite,
    (services, options) => options.Observer = new SqlLinqerDiagnostics(
        services.GetRequiredService<ILoggerFactory>().CreateLogger("SqlLinqer"),
        options: null,
        optionsProvider: () => builder.Configuration
            .GetSection("SqlLinqer:Diagnostics")
            .Get<SqlLinqerDiagnosticsOptions>() ?? new()));
```

The [minimal API sample](../../samples/SqlLinqer.MinimalApi/README.md) runs this configuration against a private SQLite fixture. The application owns providers, log destinations and trace exporters. The core library has no Microsoft logging, DI or OpenTelemetry SDK dependency.

## Local debugging

Set the query category to Debug for lifecycle, durations, error classification and known returned-row counts. Set it to Trace and opt into SQL text when inspecting generated queries:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "SqlLinqer": "Trace"
    }
  },
  "SqlLinqer": {
    "Diagnostics": {
      "IncludeSqlInLogs": true,
      "IncludeSqlInActivities": false,
      "IncludeParameterValues": false,
      "IncludeExceptionDetails": false
    }
  }
}
```

Trace alone does not opt into SQL or values. At Trace, typed parameter metadata is enabled by default. Values require `IncludeParameterValues`. The statement retains parameter placeholders. It is not an interpolated replay script. Literal values already present in raw SQL remain part of the captured statement.

Debug and Release are build configurations. They do not select runtime logger thresholds or data-detail permissions. ASP.NET Core's Development environment selects its environment-specific configuration independently. The sample's Development settings explicitly opt into Trace SQL and leave values off.

A text logger can display this sequence. These illustrative lines abbreviate SQL, timings and correlation fields; structured sinks retain the fields described below:

```text
Debug [1000] SQL query orders.search started; execution=abc123; role=Select; streaming=False
Trace [1002] SQL query orders.search; execution=abc123; role=Select: SELECT ... WHERE ... = @p0
Trace [1003] SQL query orders.search parameters; execution=abc123; role=Select: {"Items":[{"Name":"@p0","ClrType":"System.String","DbType":null,"DisplayValue":null,"IsNull":null,"Truncated":false}],"OmittedCount":0}
Debug [1001] SQL query orders.search ended with Completed; execution=abc123; role=Select; rows=12
```

The parameter record identifies its source type while leaving the value redacted. A paged query followed by a total emits another sequence with a different execution ID and role Count. At the production Warning threshold, these successful-query entries disappear and failed-query Error entries remain.

## Production baseline and incident triage

Keep application logging at Information and query logging at Warning. Default settings emit failures at Error and exclude SQL, values and full exception details:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "SqlLinqer": "Warning"
    }
  },
  "SqlLinqer": {
    "Diagnostics": {
      "SlowCommandThreshold": "00:00:01",
      "IncludeSqlInLogs": false,
      "IncludeSqlInActivities": false,
      "IncludeParameterValues": false,
      "IncludeExceptionDetails": false
    }
  }
}
```

`SlowCommandThreshold` is optional and disabled by default. It warns only after a successful operation whose provider Execute duration reaches the configured positive threshold. It does not measure subsequent reader iteration, mapping or export consumer pauses. A slow initial Execute call on a streaming query can still trigger the warning when enumeration completes. It is not a complete database-runtime measurement.

For an incident, find the stable query name, execution ID and failure classification. Temporarily enable only the required destination and detail options. Set `LogSuccessfulQueriesAtInformation` to true if every successful query summary is intentionally required at Information. Cancelled and early-stopped operations remain Debug.

## Event and field contract

| Event | Level | Meaning |
| --- | --- | --- |
| 1000 QueryStarting | Debug | One command or stream enumeration started. |
| 1001 QueryCompleted | Debug | Completed, Cancelled or StoppedEarly. Check Outcome. |
| 1001 QueryCompleted | Information | Successful completion when explicitly enabled. |
| 1001 QueryCompleted | Error | Execution, mapping, timeout or cleanup failed. |
| 1002 QuerySql | Trace | Rendered statement when IncludeSqlInLogs is true. |
| 1003 QueryParameters | Trace | Typed metadata, plus bounded values when enabled. |
| 1004 QuerySlow | Warning | Successful provider Execute duration reached the threshold. |
| 1005 DiagnosticsConfigurationInvalid | Warning | Runtime option loading failed. Redacted defaults apply until valid configuration returns. |

The integration emits no Critical events. The host determines whether a failure prevents continued service operation.

Query events 1000 through 1004 include `QueryName`, `ExecutionId`, `CommandRole`, `TraceId` and `SpanId` where available. Execution IDs exist without a tracing listener. A page and its total receive different IDs and Select/Count roles. Compiler-created regular commands also identify Insert, Update and Delete. Raw or specialized commands can retain Command. Callers can label a raw `RenderedQuery.CommandRole` explicitly without changing execution.

Completion records include `Outcome`, `DurationMs`, `CommandDurationMs`, `ReturnedRows`, `ErrorType`, `WrapperType`, `FailureKind` and `ProviderErrorCode`. ErrorType unwraps SqlResponseException. The wrapper remains available separately. FailureKind identifies known timeout, cancellation, database and JSON-mapping failures, with Execution as the fallback. Provider codes use available SQL state or numeric error-code metadata. These fields do not include exception messages.

Full exception logging remains separately opt-in. These settings govern this observer. HTTP exception handlers, provider logging and other observers have their own policies.

## Parameter detail

The structured `Parameters` field contains `Items` and `OmittedCount`. Each item contains its name, source CLR type, explicitly assigned DbType, optional DisplayValue, nullable IsNull, and Truncated flag.

Values are disabled by default. A redacted item has no DisplayValue and no IsNull assertion. When enabled, SQL NULL displays as `NULL` with IsNull true. An empty string displays as `""`. Numbers use invariant formatting, date/time values use round-trip representations, and binary displays as hexadecimal. Unknown objects display as unsupported without invoking arbitrary ToString methods.

`MaxParameterValueLength` defaults to 256 characters, with an allowed range of 16 through 65536. `MaxParameterCount` defaults to 32, with an allowed range of 1 through 1024. Large values and omitted entries are identified explicitly. These limits apply only to diagnostic capture and do not change query fields, filters or row counts. The representations are diagnostic text, not provider-ready values or SQL literals.

## Runtime configuration

Constructor options are copied and validated. Changing the original object does not change the observer. Supply `optionsProvider` to refresh options for each execution. Return a fully configured object and do not mutate it concurrently. One consistent snapshot governs the started operation, including its completion.

The host owns configuration reload and logger threshold reload. Invalid constructor limits throw immediately. If runtime option loading or validation fails, the observer uses redacted defaults, retains query failure logging, and emits one configuration warning until a valid snapshot is observed. The warning contains the configuration exception type, not its message or raw settings. Instrumentation failures do not change query results.

## Distributed tracing

Subscribe to the `SqlLinqer` ActivitySource. With OpenTelemetry, register `.AddSource(SqlLinqerDiagnostics.ActivitySourceName)` and configure exporters in the application. Activities retain request parentage and share execution IDs with logs.

`IncludeSqlInActivities` controls activity SQL independently of log thresholds and `IncludeSqlInLogs`. SQL is attached only when the listener requests activity data. Parameter values are never separate activity tags. Setting the logger category to None does not disable subscribed activities. Disable the activity destination or its SQL flag independently.

## Timing, streaming and ownership

Observation starts after query construction and rendering. Duration covers connection acquisition, execution, mapping and cleanup. It excludes observation-start formatting and logger work. CommandDuration measures provider ExecuteReader, ExecuteScalar and ExecuteNonQuery calls. It excludes later reader iteration and mapping and can be absent when no provider command ran.

ReturnedRows counts buffered collection rows or yielded root models. Scalar and affected-row results have no returned-row count. A root with many children still counts as one root.

Streams start observation on enumeration and end after disposal. Duration includes consumer time between yielded roots. Full consumption records Completed. A break records StoppedEarly. Caller cancellation records Cancelled. Execution, mapping or disposal failure records Failed. A consumer exception causes disposal and does not itself become a database failure. Cancellation effectiveness still depends on the provider and the configured [interruption hook](sync-and-async.md).

## Migration from the first diagnostics implementation

Existing event IDs 1000 through 1003 and the two-argument constructor remain available. SQL and parameter entries move from Debug to Trace. Routine completion moves from Information to Debug. `LogSuccessfulQueriesAtInformation` retains successful Information summaries when needed.

`IncludeSql` remains an alias for IncludeSqlInLogs. It no longer enables SQL in activities. Set IncludeSqlInActivities explicitly for that destination. Parameter detail is now structured and bounded rather than one flattened string. Consumers parsing log text should migrate to the structured fields.

The MVC showcase follows the same levels through its custom observer. It uses category `SqlLinqer.Showcase.Services.QueryJournal` and ActivitySource `Dispatch.SqlLinqer`. Its inspector has separate controls and is disabled by production defaults. Development enables the synthetic-data inspector explicitly. See its `docs/logging.md` for configuration.

## Custom observers and user stories

Implement `IQueryObserver` for another telemetry system. Start returns an observation or null. Complete receives the outcome after cleanup. Treat the context query and bindings as read-only. Observers must support concurrent operations and release resources if Start or Complete fails. The connector isolates observer failures from query results.

The [user-flow plan](../contributing/diagnostics-flows.md) maps adoption, local debugging, production operation, incidents, parameter conversion, exports, parallel commands and independent destinations to acceptance checks.
