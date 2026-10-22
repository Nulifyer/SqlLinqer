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
        public SqlResponse<List<T>> Execute(DbTransaction transaction = null)
            => Execute(ExecutionConnector, transaction);

        /// <inheritdoc/>
        public SqlResponse<List<T>> Execute(BaseConnector connector, DbTransaction transaction = null)
            => SelectExecution.Execute<T>(this, connector, transaction);

        /// <inheritdoc/>
        public SqlSelectResponse<T> ExecuteWithTotalCount(DbTransaction transaction = null)
            => ExecuteWithTotalCount(ExecutionConnector, transaction);

        /// <inheritdoc/>
        public SqlSelectResponse<T> ExecuteWithTotalCount(BaseConnector connector, DbTransaction transaction = null)
            => SelectExecution.ExecuteWithTotalCount<T>(this, connector, transaction);

        /// <inheritdoc/>
        public Task<SqlResponse<List<T>>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => ExecuteAsync(ExecutionConnector, transaction, cancellationToken);

        /// <inheritdoc/>
        public Task<SqlResponse<List<T>>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => SelectExecution.ExecuteAsync<T>(this, connector, transaction, cancellationToken);

        /// <inheritdoc/>
        public Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => ExecuteWithTotalCountAsync(ExecutionConnector, transaction, cancellationToken);

        /// <inheritdoc/>
        public Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => SelectExecution.ExecuteWithTotalCountAsync<T>(this, connector, transaction, cancellationToken);
    }
}
