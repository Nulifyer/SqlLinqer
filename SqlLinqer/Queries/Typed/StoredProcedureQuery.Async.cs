using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Typed
{
    public partial class StoredProcedureQuery<T>
    {
        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<List<T>>> ExecuteJsonAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteJsonAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<List<T>>> ExecuteJsonAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new System.ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            var rendered = Capture().Render(connector);
            var response = await connector.ExecuteReaderJsonAsync<T>(rendered, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<System.Data.DataTable>> ExecuteDataTableAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteDataTableAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<System.Data.DataTable>> ExecuteDataTableAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new System.ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            var rendered = Capture().Render(connector);
            var response = await connector.ExecuteReaderDataTableAsync(rendered, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }
    }
}
