using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Execution;

namespace SqlLinqer.Queries.Typed
{
    public partial class DeleteQuery<T>
    {
        /// <inheritdoc/>
        public SqlResponse<int> Execute(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            return Execute(ExecutionConnector, transaction, commandTimeout: commandTimeout);
        }
        /// <inheritdoc/>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            return ExecuteAndCapturePrimaryKeys(connector, transaction, null, commandTimeout: commandTimeout);
        }

        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            return ExecuteWithOutput(ExecutionConnector, transaction, commandTimeout: commandTimeout);
        }
        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            var rendered = this.Render(connector);
            return connector.ExecuteReaderDataTable(rendered, transaction, commandTimeout: commandTimeout);
        }

        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// </summary>
        /// <param name="connector">The connector used to execute the query.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an open connection. The caller commits, rolls back, and disposes it.</param>
        /// <param name="primaryKeys">If a list is passed, the updated primary keys will be captured in this list.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The result of the query in a state based wrapper including the affected primary keys.</returns>
        protected SqlResponse<int> ExecuteAndCapturePrimaryKeys(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null, TimeSpan? commandTimeout = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            return WriteExecution.Execute(Render(connector), connector, existingTransaction, primaryKeys, commandTimeout: commandTimeout);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAndCapturePrimaryKeysAsync(connector, transaction, null, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteWithOutputAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            var rendered = this.Render(connector);
            return await connector.ExecuteReaderDataTableAsync(rendered, transaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes the deletion asynchronously, optionally collecting returned primary keys. Requested cancellation throws.
        /// </summary>
        /// <param name="connector">The connector used for this execution.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an open connection.</param>
        /// <param name="primaryKeys">A list to receive the returned keys, or null to execute without key capture. The rendered query must select the keys when capture is requested.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The affected-row count and execution state.</returns>
        protected async Task<SqlResponse<int>> ExecuteAndCapturePrimaryKeysAsync(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            return await WriteExecution.ExecuteAsync(Render(connector), connector, existingTransaction, primaryKeys, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }
    }
}
