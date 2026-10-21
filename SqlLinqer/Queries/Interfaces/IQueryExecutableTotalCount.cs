using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a selected page and reports its total before root paging.
    /// </summary>
    /// <typeparam name="T">The declared query result type.</typeparam>
    public interface IQueryExecutableTotalCount<T>
    {
        /// <summary>
        /// Returns selected rows and the total root projection count before paging, including DISTINCT and grouping. Check the response State before reading its Result.
        /// Uses the retained connector or the current default. Data and count share one captured definition; paging runs them as separate commands.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <returns>The selected rows in Result and the unpaged projection count in TotalResults. An error clears the rows and total.</returns>
        SqlSelectResponse<T> ExecuteWithTotalCount(DbTransaction transaction = null);
        
        /// <summary>
        /// Returns selected rows and the total root projection count before paging, including DISTINCT and grouping. Check the response State before reading its Result.
        /// Data and count share one captured definition; paging runs them as separate commands.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <returns>The selected rows in Result and the unpaged projection count in TotalResults. An error clears the rows and total.</returns>
        SqlSelectResponse<T> ExecuteWithTotalCount(BaseConnector connector, DbTransaction transaction = null);
    }
}