using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Compilation;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Execution
{
    // Buffered and streaming SELECT execution is shared by model and projection queries.
    internal static class SelectExecution
    {
        internal static SqlResponse<List<T>> Execute<T>(Core.SelectQuery query, BaseConnector connector, DbTransaction transaction) where T : class
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            return connector.ExecuteReaderJson<T>(query.Render(connector), transaction);
        }

        internal static async Task<SqlResponse<List<T>>> ExecuteAsync<T>(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null) throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            return await connector.ExecuteReaderJsonAsync<T>(query.Render(connector), transaction, cancellationToken).ConfigureAwait(false);
        }

        internal static SqlSelectResponse<T> ExecuteWithTotalCount<T>(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction) where T : class
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            var snapshot = query.Capture();
            bool needsCount = NeedsCount(query);
            var response = new SqlSelectResponse<T>();
            var data = connector.ExecuteReaderJson<T>(snapshot.Render(connector), transaction);
            if (data.State == ResponseState.Error) response.SetError(data.Error);
            else if (needsCount)
            {
                var count = connector.ExecuteScalar<long>(snapshot.RenderForCount(connector.DbFlavor), transaction);
                if (count.State == ResponseState.Error) response.SetError(count.Error);
                else response.SetResult(data.Result, count.Result);
            }
            else response.SetResult(data.Result, data.Result.Count);
            return response;
        }

        internal static async Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync<T>(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            var snapshot = query.Capture();
            bool needsCount = NeedsCount(query);
            var response = new SqlSelectResponse<T>();
            var data = await connector.ExecuteReaderJsonAsync<T>(snapshot.Render(connector), transaction, cancellationToken).ConfigureAwait(false);
            if (data.State == ResponseState.Error) response.SetError(data.Error);
            else if (needsCount)
            {
                var count = await connector.ExecuteScalarAsync<long>(snapshot.RenderForCount(connector.DbFlavor), transaction, cancellationToken).ConfigureAwait(false);
                if (count.State == ResponseState.Error) response.SetError(count.Error);
                else response.SetResult(data.Result, count.Result);
            }
            else response.SetResult(data.Result, data.Result.Count);
            return response;
        }

        private static bool NeedsCount(Core.SelectQuery query)
            => query.PagingControls.Top > 0 || query.PagingControls.Limit > 0 || query.PagingControls.Offset > 0;

        internal static IAsyncEnumerable<T> ExecuteAsyncStream<T>(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction, CancellationToken cancellationToken) where T : class
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            cancellationToken.ThrowIfCancellationRequested();
            return ExecuteCapturedAsyncStream<T>(connector, query.Capture(), transaction, cancellationToken);
        }

        private static async IAsyncEnumerable<T> ExecuteCapturedAsyncStream<T>(BaseConnector connector, QuerySnapshot snapshot,
            DbTransaction transaction, [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var row in connector.ExecuteReaderJsonAsyncStream<T>(snapshot.Render(connector), transaction, cancellationToken).ConfigureAwait(false))
                yield return row;
        }
    }
}
