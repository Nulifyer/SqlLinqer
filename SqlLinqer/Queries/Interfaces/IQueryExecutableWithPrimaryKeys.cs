using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a write while collecting the affected primary keys.
    /// </summary>
    /// <typeparam name="T">The declared query result type.</typeparam>
    /// <typeparam name="TPk">The primary key type.</typeparam>
    public interface IQueryExecutableWithPrimaryKeys<T, TPk>
    {
        /// <summary>
        /// Executes a write and returns its affected-row count and affected primary keys. Check the response State before reading its Result.
        /// Uses the retained connector or the current default. MySQL modified-row output is unsupported.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <returns>The affected-row count and touched primary keys. An error response has no successful key result.</returns>
        SqlPrimaryKeysResponse<int, TPk> Execute(DbTransaction transaction = null);
        
        /// <summary>
        /// Executes a write and returns its affected-row count and affected primary keys. Check the response State before reading its Result.
        /// MySQL modified-row output is unsupported.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <returns>The affected-row count and touched primary keys. An error response has no successful key result.</returns>
        SqlPrimaryKeysResponse<int, TPk> Execute(BaseConnector connector, DbTransaction transaction = null);
    }
}