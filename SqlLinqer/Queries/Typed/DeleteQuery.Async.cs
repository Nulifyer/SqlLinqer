using System.Threading;
using System.Threading.Tasks;
using System;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Outputs;
using System.Data;

namespace SqlLinqer.Queries.Typed
{
    public partial class DeleteQuery<T>
    {
        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAndCapturePrimaryKeysAsync(connector, transaction, null, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteWithOutputAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            var rendered = this.Render(connector);
            return await connector.ExecuteReaderDataTableAsync(rendered, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes the deletion asynchronously, optionally collecting returned primary keys. Requested cancellation throws.
        /// </summary>
        /// <param name="connector">The connector used for this execution.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an open connection.</param>
        /// <param name="primaryKeys">A list to receive the returned keys, or null to execute without key capture. The rendered query must select the keys when capture is requested.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <returns>The affected-row count and execution state.</returns>
        protected async Task<SqlResponse<int>> ExecuteAndCapturePrimaryKeysAsync(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);

            var response = new SqlResponse<int>();
            ISqlResponse executedRes;
            if (primaryKeys == null)
            {
                var resNon = await connector.ExecuteNonQueryAsync(rendered, existingTransaction, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (resNon.State == ResponseState.Valid)
                    response.SetResult(resNon.Result);
                executedRes = resNon;
            }
            else
            {
                var primaryKeyData = await connector.ExecuteReaderDataTableAsync(rendered, existingTransaction, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (primaryKeyData.State == ResponseState.Valid)
                {
                    using (var table = primaryKeyData.Result)
                    foreach (System.Data.DataRow row in table.Rows)
                    {
                        primaryKeys.Add(row[0]);
                    }
                    response.SetResult(primaryKeys.Count);
                }
                executedRes = primaryKeyData;
            }

            if (executedRes.State == ResponseState.Error)
                response.SetError(executedRes.Error);

            return response;
        }
    }
}
