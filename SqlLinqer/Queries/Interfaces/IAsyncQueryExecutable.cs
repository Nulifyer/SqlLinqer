using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a typed query with a response that distinguishes a successful result from a database error. Requested cancellation propagates as an exception.
    /// </summary>
    /// <typeparam name="T">The declared query result type.</typeparam>
    public interface IAsyncQueryExecutable<T>
    {
        /// <summary>
        /// Asynchronously executes the query and returns selected data or the affected-row count. Requested cancellation throws.
        /// Uses the retained connector or the current default. Validation can throw before database execution.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <returns>The query result and execution state. An empty result or zero affected rows can be successful.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<T>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously executes the query and returns selected data or the affected-row count. Requested cancellation throws.
        /// Validation can throw before database execution.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <returns>The query result and execution state. An empty result or zero affected rows can be successful.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<T>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default);
    }
}
