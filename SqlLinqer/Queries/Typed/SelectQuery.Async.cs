using System.Threading;
using System.Threading.Tasks;
using System;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Where;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Joins;

namespace SqlLinqer.Queries.Typed
{
    public partial class SelectQuery<T>
    {
        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<List<T>>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<List<T>>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            var rendered = this.Render(connector);
            var response = await connector.ExecuteReaderJsonAsync<T>(rendered, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteWithTotalCountAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
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
