using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a command and reads a single value from its result. Requested cancellation propagates as an exception.
    /// </summary>
    public interface IAsyncQueryExecutableScalar
    {
        /// <summary>
        /// Asynchronously reads the first value of the first row and converts it to T. Without a supplied transaction, the command runs in an owned transaction unless the query declines one. Requested cancellation throws.
        /// Uses the retained connector or the current default. Validation can throw before database execution.
        /// </summary>
        /// <typeparam name="T">The scalar result type. SQL NULL is allowed only when the type can hold null.</typeparam>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The converted scalar and execution state.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<T>> ExecuteScalarAsync<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
        /// <summary>
        /// Asynchronously reads the first value of the first row and converts it to T. Without a supplied transaction, the command runs in an owned transaction unless the query declines one. Requested cancellation throws.
        /// Validation can throw before database execution.
        /// </summary>
        /// <typeparam name="T">The scalar result type. SQL NULL is allowed only when the type can hold null.</typeparam>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The converted scalar and execution state.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<T>> ExecuteScalarAsync<T>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
    }
}
