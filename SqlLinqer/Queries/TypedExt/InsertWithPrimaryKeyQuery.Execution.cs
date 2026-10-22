using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Execution;

namespace SqlLinqer.Queries.TypedExt
{
    public partial class InsertWithPrimaryKeyQuery<T, TPk>
    {
        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            return Execute(ExecutionConnector, transaction, commandTimeout: commandTimeout);
        }
        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            var primaryKeys = new List<object>();

            var baseRes = base.ExecuteAndCapturePrimaryKeys(connector, transaction, primaryKeys, commandTimeout: commandTimeout);

            return PrimaryKeyResults.Create<TPk>(baseRes, primaryKeys, connector.DbFlavor);
        }

        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> ExecuteTvp(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            return ExecuteTvp(ExecutionConnector, transaction, commandTimeout: commandTimeout);
        }
        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> ExecuteTvp(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            var primaryKeys = new List<object>();

            var baseRes = base.ExecuteTvpAndCapturePrimaryKeys(connector, transaction, primaryKeys, commandTimeout: commandTimeout);

            return PrimaryKeyResults.Create<TPk>(baseRes, primaryKeys, connector.DbFlavor);
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primaryKeys = new List<object>();

            var baseRes = await base.ExecuteAndCapturePrimaryKeysAsync(connector, transaction, primaryKeys, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);

            return PrimaryKeyResults.Create<TPk>(baseRes, primaryKeys, connector.DbFlavor);
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteTvpAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteTvpAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primaryKeys = new List<object>();

            var baseRes = await base.ExecuteTvpAndCapturePrimaryKeysAsync(connector, transaction, primaryKeys, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);

            return PrimaryKeyResults.Create<TPk>(baseRes, primaryKeys, connector.DbFlavor);
        }
    }
}
