using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Native;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        /// <summary>Executes native SELECT results and buffers complete mapped roots. Raw SQL maps flat reader columns by SQL member name.</summary>
        /// <typeparam name="T">The mapped result model.</typeparam>
        /// <param name="query">SQL, bindings, and any compiled nested result plan.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction.</param>
        /// <param name="commandTimeout">An optional execution timeout override.</param>
        /// <returns>Complete mapped roots or an execution error.</returns>
        public SqlResponse<List<T>> ExecuteReaderModel<T>(RenderedQuery query, DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null) where T : class
        {
            return ExecuteOnConnection(query, existingTransaction, false, (command, observation) =>
            {
                using (var reader = ReadCommand(command, observation))
                {
                    try
                    {
                        if (query.NativePlan?.LocalQueries != true)
                        {
                            var mapper = new NativeResultMapper<T>(query.NativePlan, DbFlavor); var rows = mapper.Read(reader);
                            return rows;
                        }
                        using (var mapper = CreateLocalNativeMapper<T>(query, command, observation))
                        {
                            var rows = mapper.Read(reader); return rows;
                        }
                    }
                    catch { StopNativeReader(command); throw; }
                }
            }, commandTimeout: commandTimeout);
        }

        /// <summary>Asynchronously buffers native SELECT results into complete mapped roots.</summary>
        /// <typeparam name="T">The mapped result model.</typeparam>
        /// <param name="query">SQL, bindings, and any compiled nested result plan.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction.</param>
        /// <param name="cancellationToken">Cancels execution and reading.</param>
        /// <param name="commandTimeout">An optional execution timeout override.</param>
        /// <returns>Complete mapped roots or an execution error. Requested cancellation throws.</returns>
        public Task<SqlResponse<List<T>>> ExecuteReaderModelAsync<T>(RenderedQuery query, DbTransaction existingTransaction = null,
            CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null) where T : class
        {
            return ExecuteOnConnectionAsync(query, existingTransaction, false, async (command, observation) =>
            {
                using (var reader = await ReadCommandAsync(command, observation, cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        if (query.NativePlan?.LocalQueries != true)
                        {
                            var mapper = new NativeResultMapper<T>(query.NativePlan, DbFlavor); var rows = await mapper.ReadAsync(reader, cancellationToken).ConfigureAwait(false);
                            return rows;
                        }
                        using (var mapper = CreateLocalNativeMapper<T>(query, command, observation))
                        {
                            var rows = await mapper.ReadAsync(reader, cancellationToken).ConfigureAwait(false); return rows;
                        }
                    }
                    catch { StopNativeReader(command); throw; }
                }
            }, cancellationToken, commandTimeout: commandTimeout);
        }

        /// <summary>Streams complete native roots without retaining previous roots. Dispose enumeration to cancel unfinished work and release owned resources.</summary>
        /// <typeparam name="T">The mapped result model.</typeparam>
        /// <param name="query">SQL, bindings, and any compiled nested result plan.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction.</param>
        /// <param name="cancellationToken">Cancels execution and reading.</param>
        /// <param name="commandTimeout">An optional execution timeout override.</param>
        /// <returns>A deferred stream that yields each complete root before reading the next root.</returns>
        public IAsyncEnumerable<T> ExecuteReaderModelAsyncStream<T>(RenderedQuery query, DbTransaction existingTransaction = null,
            CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null) where T : class
            => StartResultStream<T>(query, existingTransaction, cancellationToken, commandTimeout);

        private void StopNativeReader(DbCommand command)
        {
            using (var deadline = new CommandDeadline(command, TimeSpan.Zero, cancelCommand: Options.CancelCommand, provider: DbFlavor.ToString()))
                deadline.StopEarly();
        }

        private LocalNativeResultMapper<T> CreateLocalNativeMapper<T>(RenderedQuery query, DbCommand root, ExecutionObservation observation) where T : class
            => new LocalNativeResultMapper<T>(query.NativePlan, node =>
            {
                var command = root.Connection.CreateCommand();
                try
                {
                    ImportFromTempQueryData(command, node.LocalQuery);
                    command.CommandTimeout = root.CommandTimeout;
                    command.Transaction = root.Transaction;
                    command.Prepare();
                    return command;
                }
                catch { command.Dispose(); throw; }
            }, Options.CancelCommand, observation);
    }
}
