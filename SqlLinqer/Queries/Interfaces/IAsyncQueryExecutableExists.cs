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
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening and command execution.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        Task<SqlResponse<bool>> ExecuteExistsAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);

        /// <summary>Asynchronously checks on the supplied connector whether any row matches this query's filters. Requested cancellation throws.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening and command execution.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>True when at least one row matches, and the execution state.</returns>
        Task<SqlResponse<bool>> ExecuteExistsAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
    }
}
