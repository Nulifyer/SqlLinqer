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
    }
}
