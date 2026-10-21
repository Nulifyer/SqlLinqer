using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Components.Modeling;

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
            {
                var result = new TResult();
                int setIndex = 0;
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reader.FieldCount == 0) continue;
                        if (setIndex >= Definition.Value.ResultSets.Length)
                            throw new InvalidOperationException("The procedure returned an undeclared result set.");
                        var set = Definition.Value.ResultSets[setIndex++];
                        set.Member.SetFieldOrPropValue(result, await set.ReadAsync(reader, cancellationToken).ConfigureAwait(false));
                    } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
                }
                if (setIndex != Definition.Value.ResultSets.Length)
                    throw new InvalidOperationException($"The procedure returned {setIndex} result sets; expected {Definition.Value.ResultSets.Length}.");
                foreach (var output in plan.Outputs)
                {
                    var parameter = (DbParameter)command.Parameters[output.Key];
                    output.Value.SetFieldOrPropValue(result, CallableValue.Convert(parameter.Value, output.Value.GetFieldOrPropValueType()));
                }
                return result;
            }, cancellationToken);
        }
    }
}
