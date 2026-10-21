using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a typed query with a response that distinguishes a successful result from a database error.
    /// </summary>
    /// <typeparam name="T">The declared query result type.</typeparam>
    public interface IQueryExecutable<T>
    {
        /// <summary>
        /// Executes the query and returns selected data or the affected-row count. Check the response State before reading its Result.
        /// Uses the retained connector or the current default. Validation can throw before database execution.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <returns>The query result and execution state. An empty result or zero affected rows can be successful.</returns>
        SqlResponse<T> Execute(DbTransaction transaction = null);
        
        /// <summary>
        /// Executes the query and returns selected data or the affected-row count. Check the response State before reading its Result.
        /// Validation can throw before database execution.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <returns>The query result and execution state. An empty result or zero affected rows can be successful.</returns>
        SqlResponse<T> Execute(BaseConnector connector, DbTransaction transaction = null);
    }
}