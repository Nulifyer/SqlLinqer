using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Execution;

namespace SqlLinqer.Queries.Typed
{
    public partial class SelectAnonymousQuery<TBase, TReturn>
    {
        /// <inheritdoc/>
        public SqlResponse<List<TReturn>> Execute(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Execute(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlResponse<List<TReturn>> Execute(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => SelectExecution.Execute<TReturn>(this, connector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlSelectResponse<TReturn> ExecuteWithTotalCount(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteWithTotalCount(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlSelectResponse<TReturn> ExecuteWithTotalCount(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => SelectExecution.ExecuteWithTotalCount<TReturn>(this, connector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public Task<SqlResponse<List<TReturn>>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public Task<SqlResponse<List<TReturn>>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => SelectExecution.ExecuteAsync<TReturn>(this, connector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public Task<SqlSelectResponse<TReturn>> ExecuteWithTotalCountAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteWithTotalCountAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public Task<SqlSelectResponse<TReturn>> ExecuteWithTotalCountAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => SelectExecution.ExecuteWithTotalCountAsync<TReturn>(this, connector, transaction, cancellationToken, commandTimeout: commandTimeout);
    }
}
