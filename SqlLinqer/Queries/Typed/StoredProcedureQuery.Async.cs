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
        /// <summary>
        /// Asynchronously loads stored-procedure JSON into a list of the mapped model type.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction and open connection.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws to the caller.</param>
        /// <returns>Typed rows and execution state; an empty JSON array produces an empty list.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<SqlResponse<List<T>>> ExecuteJsonAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteJsonAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously loads stored-procedure JSON into a list of the mapped model type.
        /// </summary>
        /// <param name="connector">The execution connector for this call, leaving the retained connector unchanged.</param>
        /// <param name="transaction">An optional caller-owned transaction and open connection.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws to the caller.</param>
        /// <returns>Typed rows and execution state; an empty JSON array produces an empty list.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<SqlResponse<List<T>>> ExecuteJsonAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new System.ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = Capture().Render(connector);
            var response = await connector.ExecuteReaderJsonAsync<T>(rendered, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }

        /// <summary>
        /// Asynchronously loads a stored-procedure tabular result into a DataTable that the caller must dispose.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction and open connection.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws to the caller.</param>
        /// <returns>The buffered table and execution state. Dispose the table after a successful response.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<SqlResponse<System.Data.DataTable>> ExecuteDataTableAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteDataTableAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously loads a stored-procedure tabular result into a DataTable that the caller must dispose.
        /// </summary>
        /// <param name="connector">The execution connector for this call, leaving the retained connector unchanged.</param>
        /// <param name="transaction">An optional caller-owned transaction and open connection.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws to the caller.</param>
        /// <returns>The buffered table and execution state. Dispose the table after a successful response.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<SqlResponse<System.Data.DataTable>> ExecuteDataTableAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new System.ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = Capture().Render(connector);
            var response = await connector.ExecuteReaderDataTableAsync(rendered, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }
    }
}
