using System;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a write while collecting the affected primary keys. Requested cancellation propagates as an exception.
    /// </summary>
    /// <typeparam name="T">The declared query result type.</typeparam>
    /// <typeparam name="TPk">The primary key type.</typeparam>
    public interface IAsyncQueryExecutableWithPrimaryKeys<T, TPk>
    {
        /// <summary>
        /// Asynchronously executes a write and returns its affected-row count and affected primary keys. Requested cancellation throws.
        /// Uses the retained connector or the current default. MySQL modified-row output is unsupported.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The affected-row count and touched primary keys. An error response has no successful key result.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);

        /// <summary>
        /// Asynchronously executes a write and returns its affected-row count and affected primary keys. Requested cancellation throws.
        /// MySQL modified-row output is unsupported.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The affected-row count and touched primary keys. An error response has no successful key result.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
    }
}
