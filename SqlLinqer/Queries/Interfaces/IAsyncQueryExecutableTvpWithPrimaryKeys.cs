using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes generated-type TVP inserts and collects inserted primary keys. Requested cancellation propagates as an exception.
    /// </summary>
    /// <typeparam name="T">The declared query result type.</typeparam>
    /// <typeparam name="TPk">The primary key type.</typeparam>
    public interface IAsyncQueryExecutableTvpWithPrimaryKeys<T, TPk>
    {
        /// <summary>
        /// Asynchronously inserts through a SQL Server or Azure SQL table-valued parameter and returns affected-row count and inserted primary keys. Requested cancellation throws.
        /// Uses the retained connector or the current default. Type creation and cleanup use separate autocommit connections in the write database, requiring type-creation and schema permissions.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <returns>The inserted-row count and captured primary keys, or an error response.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteTvpAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously inserts through a SQL Server or Azure SQL table-valued parameter and returns affected-row count and inserted primary keys. Requested cancellation throws.
        /// Type creation and cleanup use separate autocommit connections in the write database, requiring type-creation and schema permissions.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <returns>The inserted-row count and captured primary keys, or an error response.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteTvpAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default);
    }
}
