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
    /// Executes a command and buffers the rows it returns as mapped lists or a DataTable. Requested cancellation propagates as an exception.
    /// </summary>
    public interface IAsyncQueryExecutableRows
    {
        /// <summary>
        /// Asynchronously maps each row to T by column name, without regard to case, or reads a single column into a scalar list. The command runs without an owned transaction. Requested cancellation throws.
        /// Uses the retained connector or the current default. Validation can throw before database execution.
        /// </summary>
        /// <typeparam name="T">A concrete mapped row type, or a scalar type for a single-column result.</typeparam>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The mapped rows and execution state. Execution or mapping failures produce an error response without partial rows.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<List<T>>> ExecuteListAsync<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
        /// <summary>
        /// Asynchronously maps each row to T by column name, without regard to case, or reads a single column into a scalar list. The command runs without an owned transaction. Requested cancellation throws.
        /// Validation can throw before database execution.
        /// </summary>
        /// <typeparam name="T">A concrete mapped row type, or a scalar type for a single-column result.</typeparam>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The mapped rows and execution state. Execution or mapping failures produce an error response without partial rows.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<List<T>>> ExecuteListAsync<T>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
        /// <summary>
        /// Asynchronously reads a command that returns several result sets and maps each one into its own collection, as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. The command runs without an owned transaction. Requested cancellation throws.
        /// Uses the retained connector or the current default. Validation can throw before database execution.
        /// </summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The mapped collections and execution state. Execution or mapping failures produce an error response without partial results.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<TResults>> ExecuteListsAsync<TResults>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
        /// <summary>
        /// Asynchronously reads a command that returns several result sets and maps each one into its own collection, as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. The command runs without an owned transaction. Requested cancellation throws.
        /// Validation can throw before database execution.
        /// </summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The mapped collections and execution state. Execution or mapping failures produce an error response without partial results.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<TResults>> ExecuteListsAsync<TResults>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
        /// <summary>
        /// Asynchronously buffers the rows in a DataTable that the caller must dispose. The command runs without an owned transaction. Requested cancellation throws.
        /// Uses the retained connector or the current default. Validation can throw before database execution.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The buffered table and execution state. Dispose a successful Result table after use.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<DataTable>> ExecuteDataTableAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
        /// <summary>
        /// Asynchronously buffers the rows in a DataTable that the caller must dispose. The command runs without an owned transaction. Requested cancellation throws.
        /// Validation can throw before database execution.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result reading. Defaults to no requested cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The buffered table and execution state. Dispose a successful Result table after use.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        Task<SqlResponse<DataTable>> ExecuteDataTableAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
    }
}
