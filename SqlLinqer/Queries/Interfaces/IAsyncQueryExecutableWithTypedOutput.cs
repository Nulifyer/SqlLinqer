using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Asynchronously executes a write and maps its Output columns onto a row type instead of a DataTable.
    /// </summary>
    public interface IAsyncQueryExecutableWithTypedOutput
    {
        /// <summary>
        /// Asynchronously executes the write and maps its Output columns onto TRow by column name. Requested cancellation throws.
        /// </summary>
        /// <typeparam name="TRow">A row class, or a scalar type for a single output column.</typeparam>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and row mapping.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The returned rows and execution state.</returns>
        Task<SqlResponse<List<TRow>>> ExecuteWithOutputAsync<TRow>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);

        /// <summary>
        /// Asynchronously executes the write on the supplied connector and maps its Output columns onto TRow by column name. Requested cancellation throws.
        /// </summary>
        /// <typeparam name="TRow">A row class, or a scalar type for a single output column.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and row mapping.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The returned rows and execution state.</returns>
        Task<SqlResponse<List<TRow>>> ExecuteWithOutputAsync<TRow>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
    }
}
