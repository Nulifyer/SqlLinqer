using System.Threading;
using System.Threading.Tasks;
using System;
using System.Data.Common;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Typed
{
    public partial class TruncateQuery<T>
    {
        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<int>> ExecuteAsync(DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await this.ExecuteAsync(ExecutionConnector, existingTransaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<int>> ExecuteAsync(BaseConnector connector, DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            var rendered = this.Render(connector);
            var response = await connector.ExecuteNonQueryAsync(rendered, existingTransaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }
    }
}
