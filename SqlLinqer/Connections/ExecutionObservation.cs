using System;
using System.Collections;
using System.Data;
using System.Diagnostics;
using SqlLinqer.Components.Render;
using SqlLinqer.Diagnostics;

namespace SqlLinqer.Connections
{
    internal sealed class ExecutionObservation
    {
        private readonly IQueryObservation observer;
        private readonly long started = Stopwatch.GetTimestamp();
        private long commandTicks;
        private bool hasCommand;
        private bool completed;
        private ExecutionObservation(IQueryObservation observer) { this.observer = observer; }
        internal static ExecutionObservation Start(IQueryObserver observer, RenderedQuery query, DbFlavor flavor, bool streaming)
        {
            if (observer == null) return null;
            try
            {
                var observation = observer.Start(new QueryExecutionContext(query, flavor, streaming));
                return observation == null ? null : new ExecutionObservation(observation);
            }
            catch { return null; }
        }
        internal void CommandFinished(long start)
        { commandTicks += Stopwatch.GetTimestamp() - start; hasCommand = true; }
        private static TimeSpan Elapsed(long ticks) => TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);
        internal void Complete<T>(SqlResponse<T> response)
        {
            if (response == null) { Complete(QueryExecutionOutcome.Completed, null, null); return; }
            if (response.State != ResponseState.Valid) { Complete(response.Error); return; }
            long? rows = response.Result is IRowCountResult counted ? counted.RowCount
                : response.Result is ICollection collection && !(response.Result is Array) ? collection.Count
                : response.Result is DataTable table ? table.Rows.Count : (long?)null;
            Complete(QueryExecutionOutcome.Completed, rows, null);
        }
        internal void Complete(Exception error)
            => Complete(error is OperationCanceledException ? QueryExecutionOutcome.Cancelled : QueryExecutionOutcome.Failed, null, error);
        internal void Complete(QueryExecutionOutcome outcome, long? rows, Exception error)
        {
            if (completed) return;
            completed = true;
            try
            {
                observer.Complete(new QueryExecutionResult(outcome, Elapsed(Stopwatch.GetTimestamp() - started),
                hasCommand ? Elapsed(commandTicks) : (TimeSpan?)null, rows, error));
            }
            catch { /* Instrumentation must not replace an execution result or error. */ }
        }
    }

    internal interface IRowCountResult
    {
        long RowCount { get; }
    }
}
