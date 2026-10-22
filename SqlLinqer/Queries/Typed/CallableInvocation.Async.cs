using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Queries.Typed
{
    public sealed partial class CallableInvocation<TCallable, TArgs, TResult>
    {
        /// <summary>
        /// Asynchronously executes the procedure and buffers every declared result set before reading outputs and return code.
        /// Uses the current default connector. Owned transactions commit after all results and outputs load; failures attempt rollback.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction with an active connection.</param>
        /// <param name="cancellationToken">Cancels opening, command execution, row reading, and result-set advancement. Requested cancellation throws.</param>
        /// <returns>The populated result model and execution state. Empty result sets become empty collections.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<TResult>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => ExecuteAsync(Default.Connector, transaction, cancellationToken);

        /// <summary>
        /// Asynchronously executes the procedure and buffers every declared result set before reading outputs and return code.
        /// Owned transactions commit after all results and outputs load; failures attempt rollback.
        /// </summary>
        /// <param name="connector">The SQL Server or Azure SQL connector, including its fallback procedure schema.</param>
        /// <param name="transaction">An optional caller-owned transaction with an active connection.</param>
        /// <param name="cancellationToken">Cancels opening, command execution, row reading, and result-set advancement. Requested cancellation throws.</param>
        /// <returns>The populated result model and execution state. Empty result sets become empty collections.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<TResult>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            var plan = Build(connector.DbFlavor, connector.Options.DefaultSchema);
            var query = SqlLinqer.Compilation.SnapshotGraph.Capture(plan.Query);
            return connector.ExecuteCallableAsync(query, transaction, async command =>
                (await ReadResultsAsync(command, plan, false, cancellationToken).ConfigureAwait(false)).Result, cancellationToken);
        }

        internal Task<SqlResponse<ProcedureResult>> ExecuteWithMetadataAsync(BaseConnector connector, DbTransaction transaction,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            var plan = Build(connector.DbFlavor, connector.Options.DefaultSchema, true);
            var query = SqlLinqer.Compilation.SnapshotGraph.Capture(plan.Query);
            return connector.ExecuteCallableAsync(query, transaction,
                command => ReadResultsAsync(command, plan, true, cancellationToken), cancellationToken);
        }

        private async Task<ProcedureResult> ReadResultsAsync(DbCommand command, Plan plan, bool metadata, CancellationToken cancellationToken)
        {
            var result = new ProcedureResult(new TResult());
            int setIndex = 0;
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.FieldCount == 0) continue;
                    if (setIndex >= _definition.ResultSets.Length)
                        throw new InvalidOperationException("The procedure returned an undeclared result set.");
                    var set = _definition.ResultSets[setIndex++];
                    set.Member.SetFieldOrPropValue(result.Result, await set.ReadAsync(reader, cancellationToken).ConfigureAwait(false));
                } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
                if (metadata && reader.RecordsAffected >= 0) result.RowsAffected = reader.RecordsAffected;
            }
            CompleteResults(command, plan, ref result, setIndex, metadata);
            return result;
        }
    }
}
