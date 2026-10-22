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
        public SqlResponse<List<TReturn>> Execute(DbTransaction transaction = null)
            => Execute(ExecutionConnector, transaction);

        /// <inheritdoc/>
        public SqlResponse<List<TReturn>> Execute(BaseConnector connector, DbTransaction transaction = null)
            => SelectExecution.Execute<TReturn>(this, connector, transaction);

        /// <inheritdoc/>
        public SqlSelectResponse<TReturn> ExecuteWithTotalCount(DbTransaction transaction = null)
            => ExecuteWithTotalCount(ExecutionConnector, transaction);

        /// <inheritdoc/>
        public SqlSelectResponse<TReturn> ExecuteWithTotalCount(BaseConnector connector, DbTransaction transaction = null)
            => SelectExecution.ExecuteWithTotalCount<TReturn>(this, connector, transaction);

        /// <inheritdoc/>
        public Task<SqlResponse<List<TReturn>>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => ExecuteAsync(ExecutionConnector, transaction, cancellationToken);

        /// <inheritdoc/>
        public Task<SqlResponse<List<TReturn>>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => SelectExecution.ExecuteAsync<TReturn>(this, connector, transaction, cancellationToken);

        /// <inheritdoc/>
        public Task<SqlSelectResponse<TReturn>> ExecuteWithTotalCountAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => ExecuteWithTotalCountAsync(ExecutionConnector, transaction, cancellationToken);

        /// <inheritdoc/>
        public Task<SqlSelectResponse<TReturn>> ExecuteWithTotalCountAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => SelectExecution.ExecuteWithTotalCountAsync<TReturn>(this, connector, transaction, cancellationToken);
    }
}
