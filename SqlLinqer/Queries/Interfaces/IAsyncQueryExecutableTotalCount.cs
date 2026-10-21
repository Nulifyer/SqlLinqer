using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a selected page and reports its total before root paging. Requested cancellation propagates as an exception.
    /// </summary>
    /// <typeparam name="T">The declared query result type.</typeparam>
    public interface IAsyncQueryExecutableTotalCount<T>
    {
        /// <summary>
        /// Asynchronously returns selected rows and the total root projection count before paging, including DISTINCT and grouping. Requested cancellation throws.
        /// Uses the retained connector or the current default. Data and count share one captured definition; paging runs them as separate commands.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <returns>The selected rows in Result and the unpaged projection count in TotalResults. An error clears the rows and total.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously returns selected rows and the total root projection count before paging, including DISTINCT and grouping. Requested cancellation throws.
        /// Data and count share one captured definition; paging runs them as separate commands.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <returns>The selected rows in Result and the unpaged projection count in TotalResults. An error clears the rows and total.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default);
    }
}
