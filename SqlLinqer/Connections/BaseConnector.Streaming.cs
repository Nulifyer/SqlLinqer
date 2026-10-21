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
        /// <summary>
        /// Streams complete typed root objects from database JSON without retaining previous rows.
        /// Execution starts on enumeration. Each enumeration executes the command again.
        /// Dispose enumeration to release the reader, command, and owned connection, including after early exit.
        /// SQL Server JSON fragments are parsed incrementally; other dialects materialize one JSON value at a time.
        /// </summary>
        /// <typeparam name="T">The mapped root row type.</typeparam>
        /// <param name="query">SQL and bindings. Keep them unchanged until all enumerations finish.</param>
        /// <param name="transaction">An optional caller-owned transaction. Do not use its connection concurrently with enumeration.</param>
        /// <param name="cancellationToken">Cancels execution and result reading.</param>
        /// <returns>A deferred sequence. Execution and mapping failures throw SqlResponseException during enumeration; requested cancellation throws.</returns>
        public IAsyncEnumerable<T> ExecuteReaderJsonAsyncStream<T>(RenderedQuery query, DbTransaction transaction = null,
            CancellationToken cancellationToken = default) where T : class
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            cancellationToken.ThrowIfCancellationRequested();
            return ExecuteCommandAsyncStream(query, token => ExecuteOnConnectionAsyncStream<T>(query, transaction, token), cancellationToken);
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
            [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ownsConnection = transaction == null;
            var connection = ownsConnection ? CreateConnection()
                : transaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    ImportFromTempQueryData(command, query);
                    PreProcessCommand(command);
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
                    // Also registers Cancel for text reads whose netstandard2.0
                    // TextReader contracts cannot receive a cancellation token.
                    using (var deadline = new CommandDeadline(command,
                        DbFlavor == DbFlavor.DuckDb ? Options.CommandTimeout : TimeSpan.Zero, cancellationToken))
                    {
                        DbDataReader reader;
                        try
                        {
                            deadline.ThrowIfInterrupted();
                            reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception error) { deadline.ThrowIfInterrupted(error); throw; }
                        using (reader)
                        {
                            var rows = CreateJsonMapper<T>().ReadAsyncStream(reader, cancellationToken).GetAsyncEnumerator(cancellationToken);
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
                                    if (!next) break;
                                    yield return rows.Current;
                                }
                            }
                            finally { await rows.DisposeAsync().ConfigureAwait(false); }
                        }
                    }
                }
            }
            finally { if (ownsConnection) connection.Dispose(); }
        }
    }
}
