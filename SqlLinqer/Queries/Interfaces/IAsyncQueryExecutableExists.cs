using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Asynchronously checks whether any row matches a query's filters without loading rows.
    /// </summary>
    public interface IAsyncQueryExecutableExists
    {
        /// <summary>Asynchronously checks whether any row matches this query's filters. Requested cancellation throws.</summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<bool>> ExecuteExistsAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);

        /// <summary>Asynchronously checks on the supplied connector whether any row matches this query's filters. Requested cancellation throws.</summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<bool>> ExecuteExistsAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
    }
}
