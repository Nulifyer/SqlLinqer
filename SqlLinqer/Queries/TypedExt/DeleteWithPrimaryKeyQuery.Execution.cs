using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Execution;

namespace SqlLinqer.Queries.TypedExt
{
    public partial class DeleteWithPrimaryKeyQuery<T, TPk>
    {
        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(DbTransaction transaction = null)
        {
            return Execute(ExecutionConnector, transaction);
        }
        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            var primaryKeys = new List<object>();

            var baseRes = base.ExecuteAndCapturePrimaryKeys(connector, transaction, primaryKeys);

            return PrimaryKeyResults.Create<TPk>(baseRes, primaryKeys, connector.DbFlavor);
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primaryKeys = new List<object>();

            var baseRes = await base.ExecuteAndCapturePrimaryKeysAsync(connector, transaction, primaryKeys, cancellationToken: cancellationToken).ConfigureAwait(false);

            return PrimaryKeyResults.Create<TPk>(baseRes, primaryKeys, connector.DbFlavor);
        }
    }
}
