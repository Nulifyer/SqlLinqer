using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Raw
{
    // Each terminal follows the library's two forms: one uses the bound or default connector, and one takes a connector.
    // To run on a connection you hold open, create the query from connector.OnConnection(connection).
    public partial class RawQuery
    {
        private static BaseConnector Require(BaseConnector connector)
            => connector ?? throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

        /// <inheritdoc/>
        public SqlResponse<int> Execute(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Execute(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteNonQuery(Render(connector), transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Require(connector).ExecuteNonQueryAsync(Render(connector), transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public SqlResponse<T> ExecuteScalar<T>(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteScalar<T>(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlResponse<T> ExecuteScalar<T>(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteScalar<T>(Render(connector), transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public async Task<SqlResponse<T>> ExecuteScalarAsync<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteScalarAsync<T>(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<T>> ExecuteScalarAsync<T>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Require(connector).ExecuteScalarAsync<T>(Render(connector), transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public SqlResponse<List<T>> ExecuteList<T>(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteList<T>(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlResponse<List<T>> ExecuteList<T>(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderList<T>(Render(connector), transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public async Task<SqlResponse<List<T>>> ExecuteListAsync<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteListAsync<T>(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<List<T>>> ExecuteListAsync<T>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Require(connector).ExecuteReaderListAsync<T>(Render(connector), transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public SqlResponse<TResults> ExecuteLists<TResults>(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteLists<TResults>(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlResponse<TResults> ExecuteLists<TResults>(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderLists<TResults>(Render(connector), transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public async Task<SqlResponse<TResults>> ExecuteListsAsync<TResults>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteListsAsync<TResults>(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<TResults>> ExecuteListsAsync<TResults>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Require(connector).ExecuteReaderListsAsync<TResults>(Render(connector), transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteDataTable(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteDataTable(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteDataTable(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderDataTable(Render(connector), transaction, commandTimeout: commandTimeout);

        /// <inheritdoc/>
        public async Task<SqlResponse<DataTable>> ExecuteDataTableAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteDataTableAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<DataTable>> ExecuteDataTableAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Require(connector).ExecuteReaderDataTableAsync(Render(connector), transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }
    }
}
