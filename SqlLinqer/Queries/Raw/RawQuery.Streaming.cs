using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Raw
{
    public partial class RawQuery
    {
        /// <inheritdoc/>
        public IAsyncEnumerable<T> ExecuteAsyncStream<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteAsyncStream<T>(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public IAsyncEnumerable<T> ExecuteAsyncStream<T>(BaseConnector connector, DbTransaction transaction = null,
            CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderListAsyncStream<T>(Render(connector), transaction, cancellationToken, commandTimeout);
    }
}
