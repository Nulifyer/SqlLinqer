using System;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Checks whether any row matches a query's filters without loading rows.
    /// </summary>
    public interface IQueryExecutableExists
    {
        /// <summary>
        /// Checks whether any row matches this query's filters. Selected fields, ordering, paging, and row locks do not apply.
        /// The check does not open an owned transaction.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        SqlResponse<bool> ExecuteExists(DbTransaction transaction = null, TimeSpan? commandTimeout = null);

        /// <summary>Checks on the supplied connector whether any row matches this query's filters.</summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        SqlResponse<bool> ExecuteExists(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null);
    }
}
