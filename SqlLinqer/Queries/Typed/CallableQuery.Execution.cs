using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Compilation;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Typed
{
    internal sealed partial class CallableQuery<TInput, TOutput>
    {
        internal SqlCallableResponse<TOutput> Execute(BaseConnector connector, DbTransaction transaction, TimeSpan? commandTimeout = null)
        {
            var prepared = Prepare(connector);
            return connector.ExecuteCallable<TOutput>(prepared, _row, transaction, commandTimeout: commandTimeout);
        }

        internal async Task<SqlCallableResponse<TOutput>> ExecuteAsync(BaseConnector connector, DbTransaction transaction, CancellationToken cancellationToken, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = Prepare(connector);
            prepared.Query = SnapshotGraph.Capture(prepared.Query);
            return await connector.ExecuteCallableAsync<TOutput>(prepared, _row, transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }
    }
}
