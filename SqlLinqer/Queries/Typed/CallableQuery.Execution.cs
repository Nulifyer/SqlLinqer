using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Compilation;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Typed
{
    internal sealed partial class CallableQuery<TInput, TOutput>
    {
        internal SqlCallableResponse<TOutput> Execute(BaseConnector connector, DbTransaction transaction)
        {
            var prepared = Prepare(connector);
            return connector.ExecuteCallable<TOutput>(prepared, _row, transaction);
        }

        internal async Task<SqlCallableResponse<TOutput>> ExecuteAsync(BaseConnector connector, DbTransaction transaction, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = Prepare(connector);
            prepared.Query = SnapshotGraph.Capture(prepared.Query);
            return await connector.ExecuteCallableAsync<TOutput>(prepared, _row, transaction, cancellationToken).ConfigureAwait(false);
        }
    }
}
