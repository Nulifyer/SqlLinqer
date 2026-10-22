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
        public IAsyncEnumerable<T> ExecuteAsyncStream(DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => ExecuteAsyncStream(ExecutionConnector, transaction, cancellationToken);

        /// <inheritdoc/>
        public IAsyncEnumerable<T> ExecuteAsyncStream(BaseConnector connector, DbTransaction transaction = null,
            CancellationToken cancellationToken = default)
            => SelectExecution.ExecuteAsyncStream<T>(this, connector, transaction, cancellationToken);
    }
}
