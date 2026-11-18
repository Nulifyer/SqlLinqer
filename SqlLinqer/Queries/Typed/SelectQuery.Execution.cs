using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Execution;

namespace SqlLinqer.Queries.Typed
{
    public partial class SelectQuery<T>
    {
        /// <inheritdoc/>
        public SqlResponse<List<T>> Execute(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Execute(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlResponse<List<T>> Execute(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => SelectExecution.Execute<T>(this, connector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlSelectResponse<T> ExecuteWithTotalCount(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteWithTotalCount(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlSelectResponse<T> ExecuteWithTotalCount(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => SelectExecution.ExecuteWithTotalCount<T>(this, connector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public Task<SqlResponse<List<T>>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public Task<SqlResponse<List<T>>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => SelectExecution.ExecuteAsync<T>(this, connector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteWithTotalCountAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => SelectExecution.ExecuteWithTotalCountAsync<T>(this, connector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>
        /// Checks whether any row matches this query's filters. Selected fields, ordering, paging, and row locks do not apply.
        /// The check does not open an owned transaction.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        public SqlResponse<bool> ExecuteExists(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteExists(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <summary>Checks on the supplied connector whether any row matches this query's filters.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        public SqlResponse<bool> ExecuteExists(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => SelectExecution.ExecuteExists(this, connector, transaction, commandTimeout: commandTimeout);

        /// <summary>Asynchronously checks whether any row matches this query's filters. Requested cancellation throws.</summary>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening and command execution.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        public Task<SqlResponse<bool>> ExecuteExistsAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteExistsAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously checks on the supplied connector whether any row matches this query's filters. Requested cancellation throws.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening and command execution.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        public Task<SqlResponse<bool>> ExecuteExistsAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => SelectExecution.ExecuteExistsAsync(this, connector, transaction, cancellationToken, commandTimeout: commandTimeout);
    }
}
