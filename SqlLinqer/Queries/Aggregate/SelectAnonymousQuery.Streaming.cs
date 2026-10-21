using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Typed
{
    public partial class SelectAnonymousQuery<TBase, TReturn>
    {
        /// <inheritdoc/>
        public IAsyncEnumerable<TReturn> ExecuteAsyncStream(DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => ExecuteAsyncStream(ExecutionConnector, transaction, cancellationToken);

        /// <inheritdoc/>
        public IAsyncEnumerable<TReturn> ExecuteAsyncStream(BaseConnector connector, DbTransaction transaction = null,
            CancellationToken cancellationToken = default)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = Capture();
            return ExecuteCapturedAsyncStream(connector, snapshot, transaction, cancellationToken);
        }

        private static async IAsyncEnumerable<TReturn> ExecuteCapturedAsyncStream(BaseConnector connector, Compilation.QuerySnapshot snapshot,
            DbTransaction transaction, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var row in connector.ExecuteReaderJsonAsyncStream<TReturn>(snapshot.Render(connector), transaction, cancellationToken).ConfigureAwait(false))
                yield return row;
        }
    }
}
