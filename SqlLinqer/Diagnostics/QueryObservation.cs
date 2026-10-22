using System;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Diagnostics
{
    /// <summary>Starts optional observations of connector executions. Observer failures do not replace database results or errors.</summary>
    public interface IQueryObserver
    {
        /// <summary>Starts one buffered operation or one stream enumeration.</summary>
        /// <param name="context">Execution metadata. Treat the rendered query and its bindings as read-only.</param>
        /// <returns>An observation, or null when this operation is not observed.</returns>
        IQueryObservation Start(QueryExecutionContext context);
    }

    /// <summary>Receives the final outcome after readers and owned resources have been released.</summary>
    public interface IQueryObservation
    {
        /// <summary>Completes an observation exactly once, including early stream disposal.</summary>
        /// <param name="result">Timing, row count, and outcome information.</param>
        void Complete(QueryExecutionResult result);
    }

    /// <summary>Describes one connector execution. Construction and SQL rendering occur before this observation starts.</summary>
    public sealed class QueryExecutionContext
    {
        /// <summary>The rendered command and bindings. Observers must not modify them.</summary>
        public RenderedQuery Query { get; }
        /// <summary>The SQL dialect selected for this execution.</summary>
        public DbFlavor DbFlavor { get; }
        /// <summary>True when the operation is a deferred stream enumeration.</summary>
        public bool IsStreaming { get; }
        internal QueryExecutionContext(RenderedQuery query, DbFlavor flavor, bool streaming)
        { Query = query; DbFlavor = flavor; IsStreaming = streaming; }
    }

    /// <summary>Distinguishes completion, failure, cancellation, and caller disposal before a stream ends.</summary>
    public enum QueryExecutionOutcome
    {
        /// <summary>The operation or enumeration finished successfully.</summary>
        Completed,
        /// <summary>Execution, mapping, or resource cleanup failed.</summary>
        Failed,
        /// <summary>The operation threw an OperationCanceledException.</summary>
        Cancelled,
        /// <summary>The caller disposed enumeration before observing its end. Consumer exceptions also cause this outcome.</summary>
        StoppedEarly
    }

    /// <summary>Reports connector lifetime separately from time spent in provider Execute calls.</summary>
    public sealed class QueryExecutionResult
    {
        /// <summary>The operation outcome.</summary>
        public QueryExecutionOutcome Outcome { get; }
        /// <summary>Time through resource cleanup. Streams include time spent processing yielded rows.</summary>
        public TimeSpan Duration { get; }
        /// <summary>Time spent in provider ExecuteReader, ExecuteScalar, and ExecuteNonQuery calls, or null if none ran. Excludes later reader iteration and mapping.</summary>
        public TimeSpan? CommandDuration { get; }
        /// <summary>Returned collection rows or yielded models, or null when the result has no known row count. This is not an affected-row count.</summary>
        public long? ReturnedRows { get; }
        /// <summary>The execution failure, or null after completion or early disposal.</summary>
        public Exception Error { get; }
        internal QueryExecutionResult(QueryExecutionOutcome outcome, TimeSpan duration, TimeSpan? commandDuration, long? rows, Exception error)
        { Outcome = outcome; Duration = duration; CommandDuration = commandDuration; ReturnedRows = rows; Error = error; }
    }
}
