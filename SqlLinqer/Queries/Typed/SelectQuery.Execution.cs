using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Typed
{
    public partial class SelectQuery<T>
    {
        /// <inheritdoc/>
        public SqlResponse<List<T>> Execute(DbTransaction transaction = null)
        {
            return Execute(ExecutionConnector, transaction);
        }

        /// <inheritdoc/>
        public SqlResponse<List<T>> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);
            var response = connector.ExecuteReaderJson<T>(rendered, transaction);

            return response;
        }

        /// <inheritdoc/>
        public SqlSelectResponse<T> ExecuteWithTotalCount(DbTransaction transaction = null)
        {
            return ExecuteWithTotalCount(ExecutionConnector, transaction);
        }

        /// <inheritdoc/>
        public SqlSelectResponse<T> ExecuteWithTotalCount(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            var snapshot = Capture();
            bool needsCount = PagingControls.Top > 0 || PagingControls.Limit > 0 || PagingControls.Offset > 0;
            var selectRes = new SqlSelectResponse<T>();

            var dataRes = connector.ExecuteReaderJson<T>(snapshot.Render(connector), transaction);
            if (dataRes.State == ResponseState.Error)
            {
                selectRes.SetError(dataRes.Error);
                return selectRes;
            }

            if (needsCount)
            {
                var count_query = snapshot.RenderForCount(connector.DbFlavor);
                var countRes = connector.ExecuteScalar<long>(count_query, transaction);
                if (countRes.State == ResponseState.Error)
                {
                    selectRes.SetError(countRes.Error);
                    return selectRes;
                }

                selectRes.SetResult(dataRes.Result, countRes.Result);
            }
            else
            {
                selectRes.SetResult(dataRes.Result, dataRes.Result.Count);
            }

            return selectRes;
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<List<T>>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<List<T>>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);
            var response = await connector.ExecuteReaderJsonAsync<T>(rendered, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }

        /// <inheritdoc/>
        public async Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteWithTotalCountAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            var snapshot = Capture();
            bool needsCount = PagingControls.Top > 0 || PagingControls.Limit > 0 || PagingControls.Offset > 0;
            var selectRes = new SqlSelectResponse<T>();

            var dataRes = await connector.ExecuteReaderJsonAsync<T>(snapshot.Render(connector), transaction, cancellationToken).ConfigureAwait(false);
            if (dataRes.State == ResponseState.Error)
            {
                selectRes.SetError(dataRes.Error);
                return selectRes;
            }

            if (needsCount)
            {
                var count_query = snapshot.RenderForCount(connector.DbFlavor);
                var countRes = await connector.ExecuteScalarAsync<long>(count_query, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (countRes.State == ResponseState.Error)
                {
                    selectRes.SetError(countRes.Error);
                    return selectRes;
                }

                selectRes.SetResult(dataRes.Result, countRes.Result);
            }
            else
            {
                selectRes.SetResult(dataRes.Result, dataRes.Result.Count);
            }

            return selectRes;
        }
    }
}
