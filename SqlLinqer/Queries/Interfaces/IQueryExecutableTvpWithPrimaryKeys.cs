using System;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes generated-type TVP inserts and collects inserted primary keys.
    /// </summary>
    /// <typeparam name="T">The declared query result type.</typeparam>
    /// <typeparam name="TPk">The primary key type.</typeparam>
    public interface IQueryExecutableTvpWithPrimaryKeys<T, TPk>
    {
        /// <summary>
        /// Inserts through a SQL Server or Azure SQL table-valued parameter and returns affected-row count and inserted primary keys. Check the response State before reading its Result.
        /// Uses the retained connector or the current default. Type creation and cleanup use separate autocommit connections in the write database, requiring type-creation and schema permissions.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The inserted-row count and captured primary keys, or an error response.</returns>
        SqlPrimaryKeysResponse<int, TPk> ExecuteTvp(DbTransaction transaction = null, TimeSpan? commandTimeout = null);

        /// <summary>
        /// Inserts through a SQL Server or Azure SQL table-valued parameter and returns affected-row count and inserted primary keys. Check the response State before reading its Result.
        /// Type creation and cleanup use separate autocommit connections in the write database, requiring type-creation and schema permissions.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The inserted-row count and captured primary keys, or an error response.</returns>
        SqlPrimaryKeysResponse<int, TPk> ExecuteTvp(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null);
    }
}
