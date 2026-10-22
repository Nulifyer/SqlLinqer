using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SqlLinqer.Diagnostics
{
    /// <summary>Observes executions through ILogger and an ActivitySource compatible with OpenTelemetry listeners.</summary>
    public sealed class SqlLinqerDiagnostics : IQueryObserver
    {
        /// <summary>The ActivitySource name to register with an OpenTelemetry tracer provider.</summary>
        public const string ActivitySourceName = "SqlLinqer";
        private static readonly ActivitySource Source = new ActivitySource(ActivitySourceName);
        private readonly ILogger logger;
        private readonly SqlLinqerDiagnosticsOptions options;

        /// <summary>Creates an observer with optional structured logging and opt-in SQL details.</summary>
        public SqlLinqerDiagnostics(ILogger logger = null, SqlLinqerDiagnosticsOptions options = null)
        { this.logger = logger ?? NullLogger.Instance; this.options = options ?? new SqlLinqerDiagnosticsOptions(); }

        /// <summary>Starts one activity and observes the operation until its resources are released. Returns null when neither logs nor activities are enabled.</summary>
        public IQueryObservation Start(QueryExecutionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!Source.HasListeners() && !logger.IsEnabled(LogLevel.Information) && !logger.IsEnabled(LogLevel.Debug)
                && !logger.IsEnabled(LogLevel.Error)) return null;
            string name = context.Query.QueryName ?? (context.IsStreaming ? "sql.stream" : "sql.execute");
            var activity = Source.StartActivity(name, ActivityKind.Client);
            try
            {
                activity?.SetTag("db.system.name", SystemName(context.DbFlavor));
                activity?.SetTag("sqllinqer.streaming", context.IsStreaming);
                if (options.IncludeSql && activity?.IsAllDataRequested == true)
                    activity.SetTag("db.query.text", context.Query.Text);
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(new EventId(1000, "QueryStarting"), "SQL query {QueryName} started; streaming={Streaming}", name, context.IsStreaming);
                    if (options.IncludeSql) logger.LogDebug(new EventId(1002, "QuerySql"), "SQL query {QueryName}: {CommandText}", name, context.Query.Text);
                    if (options.IncludeParameterValues)
                        logger.LogDebug(new EventId(1003, "QueryParameters"), "SQL query {QueryName} parameters: {Parameters}", name,
                            string.Join(", ", context.Query.Parameters.GetAll().Select(p => p.Placehodler + "=" + p.Value)));
                }
                return new Observation(logger, options, name, activity);
            }
            catch { activity?.Dispose(); throw; }
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
            private readonly string name;
            private readonly Activity activity;
            internal Observation(ILogger logger, SqlLinqerDiagnosticsOptions options, string name, Activity activity)
            { this.logger = logger; this.options = options; this.name = name; this.activity = activity; }
            public void Complete(QueryExecutionResult result)
            {
                try
                {
                    activity?.SetTag("sqllinqer.outcome", result.Outcome.ToString());
                    activity?.SetTag("sqllinqer.duration_ms", result.Duration.TotalMilliseconds);
                    if (result.CommandDuration.HasValue) activity?.SetTag("sqllinqer.command.duration_ms", result.CommandDuration.Value.TotalMilliseconds);
                    if (result.ReturnedRows.HasValue) activity?.SetTag("db.response.returned_rows", result.ReturnedRows.Value);
                    if (result.Outcome == QueryExecutionOutcome.Failed)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error);
                        activity?.SetTag("error.type", result.Error?.GetType().FullName);
                    }
                    else if (result.Outcome == QueryExecutionOutcome.Completed) activity?.SetStatus(ActivityStatusCode.Ok);
                    var level = result.Outcome == QueryExecutionOutcome.Failed ? LogLevel.Error : LogLevel.Information;
                    if (logger.IsEnabled(level)) logger.Log(level, new EventId(1001, "QueryCompleted"),
                        options.IncludeExceptionDetails ? result.Error : null,
                        "SQL query {QueryName} ended with {Outcome} in {DurationMs} ms; command={CommandDurationMs} ms; rows={ReturnedRows}; error={ErrorType}",
                        name, result.Outcome, result.Duration.TotalMilliseconds, result.CommandDuration?.TotalMilliseconds,
                        result.ReturnedRows, result.Error?.GetType().FullName);
                }
                finally { activity?.Dispose(); }
            }
        }
    }
}
