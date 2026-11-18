using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;
using SqlLinqer.Exceptions;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        // Maps the rows of an open reader. Native SELECT results and flat tabular rows differ only here.
        private delegate IAsyncEnumerable<T> StreamRows<T>(DbCommand command, DbDataReader reader, ExecutionObservation observation, CancellationToken cancellationToken);

        private IAsyncEnumerable<T> StartResultStream<T>(RenderedQuery query, DbTransaction transaction,
            CancellationToken cancellationToken, TimeSpan? commandTimeout) where T : class
            => StartStream<T>(query, transaction, (command, reader, observation, token) => ReadNativeRows<T>(command, query, reader, observation, token),
                CommandBehavior.SequentialAccess, cancellationToken, commandTimeout);

        private IAsyncEnumerable<T> StartStream<T>(RenderedQuery query, DbTransaction transaction,
            StreamRows<T> rows, CommandBehavior behavior, CancellationToken cancellationToken, TimeSpan? commandTimeout)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            cancellationToken.ThrowIfCancellationRequested();
            commandTimeout = ResolveCommandTimeout(commandTimeout);
            if (Options.Observer != null)
                return ObserveStream(query, transaction, rows, behavior, cancellationToken, commandTimeout);
            return ExecuteCommandAsyncStream(query, token => ExecuteOnConnectionAsyncStream(query, transaction, rows, behavior, token,
                commandTimeout: commandTimeout), cancellationToken);
        }

        private async IAsyncEnumerable<T> ObserveStream<T>(RenderedQuery query, DbTransaction transaction,
            StreamRows<T> mapRows, CommandBehavior behavior, [EnumeratorCancellation] CancellationToken cancellationToken, TimeSpan? commandTimeout)
        {
            var observation = ExecutionObservation.Start(Options.Observer, query, DbFlavor, true);
            Exception failure = null;
            long count = 0;
            bool completed = false;
            IAsyncEnumerator<T> rows = null;
            try
            {
                try
                {
                    rows = ExecuteCommandAsyncStream(query, token => ExecuteOnConnectionAsyncStream(query, transaction, mapRows, behavior, token,
                        commandTimeout, observation), cancellationToken).GetAsyncEnumerator(cancellationToken);
                }
                catch (Exception error) { failure = error; throw; }
                while (true)
                {
                    bool next;
                    try { next = await rows.MoveNextAsync().ConfigureAwait(false); }
                    catch (Exception error) { failure = error; throw; }
                    if (!next) { completed = true; break; }
                    count++;
                    yield return rows.Current;
                }
            }
            finally
            {
                try { if (rows != null) await rows.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { failure = error; throw; }
                finally
                {
                    observation?.Complete(failure is OperationCanceledException ? SqlLinqer.Diagnostics.QueryExecutionOutcome.Cancelled
                        : failure != null ? SqlLinqer.Diagnostics.QueryExecutionOutcome.Failed
                        : completed ? SqlLinqer.Diagnostics.QueryExecutionOutcome.Completed : SqlLinqer.Diagnostics.QueryExecutionOutcome.StoppedEarly,
                        count, failure);
                }
            }
        }

        /// <summary>
        /// Intercepts streaming execution, including enumeration and disposal. Override this hook for streaming logging or policies.
        /// Buffered ExecuteCommand and ExecuteCommandAsync overrides do not intercept streams. Do not retry after yielding rows.
        /// </summary>
        /// <typeparam name="T">The streamed row type.</typeparam>
        /// <param name="query">The SQL and bindings being executed.</param>
        /// <param name="action">Creates the deferred execution with the enumeration cancellation token.</param>
        /// <param name="cancellationToken">Cancels command execution and enumeration.</param>
        /// <returns>The intercepted sequence with execution failures wrapped in SqlResponseException.</returns>
        protected virtual async IAsyncEnumerable<T> ExecuteCommandAsyncStream<T>(RenderedQuery query,
            Func<CancellationToken, IAsyncEnumerable<T>> action,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var rows = action(cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool next;
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        next = await rows.MoveNextAsync().ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (Exception error) { throw StreamingError(query, error, cancellationToken); }
                    if (!next) break;
                    yield return rows.Current;
                }
            }
            finally
            {
                try { await rows.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { throw StreamingError(query, error, cancellationToken); }
            }
        }

        private static Exception StreamingError(RenderedQuery query, Exception error, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return new OperationCanceledException("The database operation was cancelled.", error, cancellationToken);
            return error is SqlResponseException ? error : new SqlResponseException(query, error);
        }

        private async IAsyncEnumerable<T> ExecuteOnConnectionAsyncStream<T>(RenderedQuery query, DbTransaction transaction,
            StreamRows<T> rows, CommandBehavior behavior,
            [EnumeratorCancellation] CancellationToken cancellationToken, TimeSpan? commandTimeout = null, ExecutionObservation observation = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = ResolveConnection(transaction, out bool ownsConnection);
            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    ImportFromTempQueryData(command, query);
                    var timeout = commandTimeout.Value;
                    PreProcessCommand(command, timeout);
                    if (DbFlavor == DbFlavor.DuckDb)
                    {
                        // SequentialAccess alone does not enable DuckDB.NET's
                        // native streaming result. Keep the library provider-neutral.
                        var streaming = command.GetType().GetProperty("UseStreamingMode");
                        if (streaming == null || streaming.PropertyType != typeof(bool) || !streaming.CanWrite)
                            throw new NotSupportedException("DuckDB streaming requires a provider with a writable Boolean UseStreamingMode property.");
                        streaming.SetValue(command, true);
                    }
                    command.Transaction = transaction;
                    var sequence = ReadCommandStream(command, rows, behavior, timeout, observation, cancellationToken);
                    await foreach (var row in sequence.ConfigureAwait(false)) yield return row;
                }
            }
            finally { if (ownsConnection) connection.Dispose(); }
        }

        private async IAsyncEnumerable<T> ReadCommandStream<T>(DbCommand command, StreamRows<T> mapRows, CommandBehavior behavior,
            TimeSpan timeout, ExecutionObservation observation, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Also registers Cancel for text reads whose netstandard2.0
            // TextReader contracts cannot receive a cancellation token.
            using (var deadline = new CommandDeadline(command,
                DbFlavor == DbFlavor.DuckDb || Options.CancelCommand != null ? timeout : TimeSpan.Zero,
                cancellationToken, Options.CancelCommand, DbFlavor == DbFlavor.DuckDb ? "DuckDB" : DbFlavor.ToString()))
            {
                DbDataReader reader;
                try
                {
                    deadline.ThrowIfInterrupted();
                    reader = await ReadCommandAsync(command, observation, cancellationToken, behavior).ConfigureAwait(false);
                }
                catch (Exception error) { deadline.ThrowIfInterrupted(error); throw; }
                using (reader)
                {
                    var rows = mapRows(command, reader, observation, cancellationToken).GetAsyncEnumerator(cancellationToken);
                    bool completed = false;
                    try
                    {
                        while (true)
                        {
                            bool next;
                            try
                            {
                                deadline.ThrowIfInterrupted();
                                next = await rows.MoveNextAsync().ConfigureAwait(false);
                                deadline.ThrowIfInterrupted();
                            }
                            catch (Exception error) { deadline.ThrowIfInterrupted(error); throw; }
                            if (!next) { completed = true; break; }
                            yield return rows.Current;
                        }
                    }
                    finally
                    {
                        if (!completed) deadline.StopEarly();
                        await rows.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        // Disposing this sequence releases the child-query mapper after its rows, and before the reader closes.
        private async IAsyncEnumerable<T> ReadNativeRows<T>(DbCommand command, RenderedQuery query, DbDataReader reader,
            ExecutionObservation observation, [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
        {
            using (var localMapper = query.NativePlan?.LocalQueries == true ? CreateLocalNativeMapper<T>(query, command, observation) : null)
            {
                var sequence = localMapper != null ? localMapper.ReadAsyncStream(reader, cancellationToken)
                    : new SqlLinqer.Components.Native.NativeResultMapper<T>(query.NativePlan, DbFlavor).ReadAsyncStream(reader, cancellationToken);
                await foreach (var row in sequence.ConfigureAwait(false)) yield return row;
            }
        }

        // Maps flat rows as ExecuteReaderList does, one row at a time.
        private async IAsyncEnumerable<T> ReadTabularRows<T>(DbDataReader reader, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (reader.FieldCount == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)) yield break;
            }
            var rowType = SqlLinqer.Components.Modeling.TabularRowType.GetCached(typeof(T));
            var ordinals = rowType.GetOrdinals(reader);
            var values = new object[reader.FieldCount];
            var mapped = rowType.CreateBuffer();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadRowValues(reader, values);
                yield return (T)rowType.Read(reader, ordinals, values, mapped);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Streams the rows of a tabular command without buffering them. Rows map as ExecuteReaderList maps them.
        /// The command runs on enumeration, without an owned transaction. Dispose enumeration to cancel unfinished work and release owned resources.
        /// </summary>
        /// <typeparam name="T">A concrete mapped row type, or a scalar type for a single-column result.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction. Keep its connection available until enumeration is disposed.</param>
        /// <param name="cancellationToken">Cancels execution and reading.</param>
        /// <param name="commandTimeout">An optional execution timeout override.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration; requested cancellation throws.</returns>
        public IAsyncEnumerable<T> ExecuteReaderListAsyncStream<T>(RenderedQuery query, DbTransaction existingTransaction = null,
            CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => StartStream<T>(query, existingTransaction, (command, reader, observation, token) => ReadTabularRows<T>(reader, token),
                CommandBehavior.Default, cancellationToken, commandTimeout);
    }
}
