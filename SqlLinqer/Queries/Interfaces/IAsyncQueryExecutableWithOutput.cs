using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a write with a buffered modified-row projection. Requested cancellation propagates as an exception.
    /// </summary>
    public interface IAsyncQueryExecutableWithOutput
    {
        /// <summary>
        /// Asynchronously executes a write and buffers the selected modified columns in a DataTable that the caller must dispose. Requested cancellation throws.
        /// Uses the retained connector or the current default. MySQL modified-row output is unsupported.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <returns>The buffered modified rows and execution state. Dispose the Result table after a successful response.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously executes a write and buffers the selected modified columns in a DataTable that the caller must dispose. Requested cancellation throws.
        /// MySQL modified-row output is unsupported.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <returns>The buffered modified rows and execution state. Dispose the Result table after a successful response.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default);
    }
}
