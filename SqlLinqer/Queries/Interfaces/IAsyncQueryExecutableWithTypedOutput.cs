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
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The returned rows and execution state.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<List<TRow>>> ExecuteWithOutputAsync<TRow>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);

        /// <summary>
        /// Asynchronously executes the write on the supplied connector and maps its Output columns onto TRow by column name. Requested cancellation throws.
        /// </summary>
        /// <typeparam name="TRow">A row class, or a scalar type for a single output column.</typeparam>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The returned rows and execution state.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<List<TRow>>> ExecuteWithOutputAsync<TRow>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
    }
}
