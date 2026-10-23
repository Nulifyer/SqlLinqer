using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SqlLinqer.Diagnostics
{
    /// <summary>Observes executions through ILogger and an ActivitySource compatible with OpenTelemetry listeners.</summary>
    public sealed class SqlLinqerDiagnostics : IQueryObserver
    {
        /// <summary>The ActivitySource name to register with an OpenTelemetry tracer provider.</summary>
        public const string ActivitySourceName = "SqlLinqer";
        /// <summary>The recommended logger category for category-specific runtime filtering.</summary>
        public const string LoggerCategoryName = "SqlLinqer";
        private static readonly ActivitySource Source = new ActivitySource(ActivitySourceName);
        private readonly ILogger logger;
        private readonly SqlLinqerDiagnosticsOptions options;
        private readonly Func<SqlLinqerDiagnosticsOptions> optionsProvider;
        private int invalidConfigurationReported;
        private static readonly SqlLinqerDiagnosticsOptions RedactedDefaults = new SqlLinqerDiagnosticsOptions();

        /// <summary>Creates an observer with optional structured logging and independently enabled details.</summary>
        /// <param name="logger">An application logger. Null disables ILogger output.</param>
        /// <param name="options">Initial options, copied and validated at construction.</param>
        public SqlLinqerDiagnostics(ILogger logger = null, SqlLinqerDiagnosticsOptions options = null)
            : this(logger, options, null) { }

        /// <summary>Creates an observer whose options can follow application-owned runtime configuration.</summary>
        /// <param name="logger">The application logger, or null to disable ILogger output.</param>
        /// <param name="options">Validated fallback settings, or null for defaults.</param>
        /// <param name="optionsProvider">Current-options factory evaluated and copied at the start of each observed operation. Return a fully configured object and do not mutate it concurrently.</param>
        public SqlLinqerDiagnostics(ILogger logger, SqlLinqerDiagnosticsOptions options,
            Func<SqlLinqerDiagnosticsOptions> optionsProvider)
        {
            this.logger = logger ?? NullLogger.Instance;
            this.options = (options ?? new SqlLinqerDiagnosticsOptions()).Snapshot();
            this.optionsProvider = optionsProvider;
        }

        /// <summary>Starts one activity and observes the operation through resource cleanup. Returns null when all destinations are disabled.</summary>
        public IQueryObservation Start(QueryExecutionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!Source.HasListeners() && !logger.IsEnabled(LogLevel.Trace) && !logger.IsEnabled(LogLevel.Debug)
                && !logger.IsEnabled(LogLevel.Information) && !logger.IsEnabled(LogLevel.Warning) && !logger.IsEnabled(LogLevel.Error)) return null;
            var settings = ReadOptions();
            string name = context.Query.QueryName ?? (context.IsStreaming ? "sql.stream" : "sql.execute");
            var activity = Source.StartActivity(name, ActivityKind.Client);
            try
            {
                string executionId = context.ExecutionId;
                string role = context.CommandRole;
                var parent = activity ?? Activity.Current;
                string traceId = parent?.TraceId.ToString(), spanId = parent?.SpanId.ToString();
                activity?.SetTag("db.system.name", SystemName(context.DbFlavor));
                activity?.SetTag("sqllinqer.execution_id", executionId);
                activity?.SetTag("sqllinqer.command.role", role);
                activity?.SetTag("sqllinqer.streaming", context.IsStreaming);
                if (settings.IncludeSqlInActivities && activity?.IsAllDataRequested == true)
                    activity.SetTag("db.query.text", context.Query.Text);
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug(new EventId(1000, "QueryStarting"),
                        "SQL query {QueryName} started; execution={ExecutionId}; role={CommandRole}; streaming={Streaming}; trace={TraceId}; span={SpanId}",
                        name, executionId, role, context.IsStreaming, traceId, spanId);
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    if (settings.IncludeSqlInLogs)
                        logger.LogTrace(new EventId(1002, "QuerySql"),
                            "SQL query {QueryName}; execution={ExecutionId}; role={CommandRole}; trace={TraceId}; span={SpanId}: {CommandText}",
                            name, executionId, role, traceId, spanId, context.Query.Text);
                    if (settings.IncludeParameterMetadata || settings.IncludeParameterValues)
                        logger.LogTrace(new EventId(1003, "QueryParameters"),
                            "SQL query {QueryName} parameters; execution={ExecutionId}; role={CommandRole}; trace={TraceId}; span={SpanId}: {Parameters}",
                            name, executionId, role, traceId, spanId, DiagnosticParameterSet.Capture(context.Query, settings));
                }
                return new Observation(logger, settings, name, executionId, role, traceId, spanId, activity);
            }
            catch { activity?.Dispose(); throw; }
        }

        private SqlLinqerDiagnosticsOptions ReadOptions()
        {
            if (optionsProvider == null) return options;
            try
            {
                var settings = (optionsProvider() ?? options).Snapshot();
                Interlocked.Exchange(ref invalidConfigurationReported, 0);
                return settings;
            }
            catch (Exception error)
            {
                if (Interlocked.Exchange(ref invalidConfigurationReported, 1) == 0)
                {
                    try
                    {
                        if (logger.IsEnabled(LogLevel.Warning)) logger.LogWarning(new EventId(1005, "DiagnosticsConfigurationInvalid"),
                            "SQL diagnostics configuration is invalid; using redacted defaults; error={ErrorType}", error.GetType().FullName);
                    }
                    catch { }
                }
                return RedactedDefaults;
            }
        }

        private static string SystemName(DbFlavor flavor)
        {
            switch (flavor)
            {
                case DbFlavor.SqlServer: case DbFlavor.AzureSql: return "mssql";
                case DbFlavor.PostgreSql: return "postgresql";
                case DbFlavor.MySql: return "mysql";
                case DbFlavor.Sqlite: return "sqlite";
                case DbFlavor.DuckDb: return "duckdb";
                default: return flavor.ToString();
            }
        }

        private sealed class Observation : IQueryObservation
        {
            private readonly ILogger logger;
            private readonly SqlLinqerDiagnosticsOptions options;
            private readonly string name, executionId, role, traceId, spanId;
            private readonly Activity activity;
            internal Observation(ILogger logger, SqlLinqerDiagnosticsOptions options, string name,
                string executionId, string role, string traceId, string spanId, Activity activity)
            { this.logger = logger; this.options = options; this.name = name; this.executionId = executionId; this.role = role; this.traceId = traceId; this.spanId = spanId; this.activity = activity; }
            public void Complete(QueryExecutionResult result)
            {
                try
                {
                    var failure = DiagnosticFailure.Capture(result.Error);
                    activity?.SetTag("sqllinqer.outcome", result.Outcome.ToString());
                    activity?.SetTag("sqllinqer.duration_ms", result.Duration.TotalMilliseconds);
                    if (result.CommandDuration.HasValue) activity?.SetTag("sqllinqer.command.duration_ms", result.CommandDuration.Value.TotalMilliseconds);
                    if (result.ReturnedRows.HasValue) activity?.SetTag("db.response.returned_rows", result.ReturnedRows.Value);
                    if (result.Outcome == QueryExecutionOutcome.Failed)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error);
                        activity?.SetTag("error.type", failure.ErrorType);
                        activity?.SetTag("sqllinqer.error.kind", failure.Kind);
                        activity?.SetTag("sqllinqer.error.provider_code", failure.ProviderCode);
                    }
                    else if (result.Outcome == QueryExecutionOutcome.Completed) activity?.SetStatus(ActivityStatusCode.Ok);
                    var level = result.Outcome == QueryExecutionOutcome.Failed ? LogLevel.Error
                        : result.Outcome == QueryExecutionOutcome.Completed && options.LogSuccessfulQueriesAtInformation ? LogLevel.Information : LogLevel.Debug;
                    if (logger.IsEnabled(level)) logger.Log(level, new EventId(1001, "QueryCompleted"),
                        options.IncludeExceptionDetails ? result.Error : null,
                        "SQL query {QueryName} ended with {Outcome}; execution={ExecutionId}; role={CommandRole}; duration={DurationMs} ms; command={CommandDurationMs} ms; rows={ReturnedRows}; error={ErrorType}; wrapper={WrapperType}; kind={FailureKind}; provider_code={ProviderErrorCode}; trace={TraceId}; span={SpanId}",
                        name, result.Outcome, executionId, role, result.Duration.TotalMilliseconds, result.CommandDuration?.TotalMilliseconds,
                        result.ReturnedRows, failure.ErrorType, failure.WrapperType, failure.Kind, failure.ProviderCode, traceId, spanId);
                    if (options.SlowCommandThreshold.HasValue && result.Outcome == QueryExecutionOutcome.Completed
                        && result.CommandDuration >= options.SlowCommandThreshold && logger.IsEnabled(LogLevel.Warning))
                        logger.LogWarning(new EventId(1004, "QuerySlow"),
                            "SQL query {QueryName} exceeded its provider Execute threshold; execution={ExecutionId}; role={CommandRole}; phase={TimingPhase}; command={CommandDurationMs} ms; threshold={ThresholdMs} ms; trace={TraceId}; span={SpanId}",
                            name, executionId, role, "ProviderExecute", result.CommandDuration.Value.TotalMilliseconds,
                            options.SlowCommandThreshold.Value.TotalMilliseconds, traceId, spanId);
                }
                finally { activity?.Dispose(); }
            }
        }
    }
}
