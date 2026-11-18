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
        internal static SqlResponse<List<T>> Execute<T>(Core.SelectQuery query, BaseConnector connector, DbTransaction transaction, TimeSpan? commandTimeout = null) where T : class
        {
            SqlLinqer.Connections.BaseConnector.Require(connector);
            return connector.ExecuteReaderModel<T>(query.Capture().RenderForExecution<T>(connector), transaction, commandTimeout);
        }

        internal static async Task<SqlResponse<List<T>>> ExecuteAsync<T>(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction, CancellationToken cancellationToken, TimeSpan? commandTimeout = null) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqlLinqer.Connections.BaseConnector.Require(connector);
            return await connector.ExecuteReaderModelAsync<T>(query.Capture().RenderForExecution<T>(connector), transaction, cancellationToken, commandTimeout).ConfigureAwait(false);
        }

        internal static SqlSelectResponse<T> ExecuteWithTotalCount<T>(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction, TimeSpan? commandTimeout = null) where T : class
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            commandTimeout = connector.ResolveCommandTimeout(commandTimeout);
            var snapshot = query.Capture();
            bool needsCount = NeedsCount(query);
            var response = new SqlSelectResponse<T>();
            var data = connector.ExecuteReaderModel<T>(snapshot.RenderForExecution<T>(connector), transaction, commandTimeout);
            if (data.State == ResponseState.Error) response.SetError(data.Error);
            else if (needsCount)
            {
                var count = connector.ExecuteScalar<long>(snapshot.RenderForCount(connector.DbFlavor), transaction, commandTimeout: commandTimeout);
                if (count.State == ResponseState.Error) response.SetError(count.Error);
                else response.SetResult(data.Result, count.Result);
            }
            else response.SetResult(data.Result, data.Result.Count);
            return response;
        }

        internal static async Task<SqlSelectResponse<T>> ExecuteWithTotalCountAsync<T>(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction, CancellationToken cancellationToken, TimeSpan? commandTimeout = null) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            commandTimeout = connector.ResolveCommandTimeout(commandTimeout);
            var snapshot = query.Capture();
            bool needsCount = NeedsCount(query);
            var response = new SqlSelectResponse<T>();
            var data = await connector.ExecuteReaderModelAsync<T>(snapshot.RenderForExecution<T>(connector), transaction, cancellationToken, commandTimeout).ConfigureAwait(false);
            if (data.State == ResponseState.Error) response.SetError(data.Error);
            else if (needsCount)
            {
                var count = await connector.ExecuteScalarAsync<long>(snapshot.RenderForCount(connector.DbFlavor), transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
                if (count.State == ResponseState.Error) response.SetError(count.Error);
                else response.SetResult(data.Result, count.Result);
            }
            else response.SetResult(data.Result, data.Result.Count);
            return response;
        }

        internal static SqlResponse<bool> ExecuteExists(Core.SelectQuery query, BaseConnector connector, DbTransaction transaction, TimeSpan? commandTimeout = null)
        {
            SqlLinqer.Connections.BaseConnector.Require(connector);
            return ToExists(connector.ExecuteScalar<int>(query.Capture().RenderForExists(connector.DbFlavor), transaction, commandTimeout: commandTimeout));
        }

        internal static async Task<SqlResponse<bool>> ExecuteExistsAsync(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction, CancellationToken cancellationToken, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqlLinqer.Connections.BaseConnector.Require(connector);
            return ToExists(await connector.ExecuteScalarAsync<int>(query.Capture().RenderForExists(connector.DbFlavor), transaction,
                cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false));
        }

        private static SqlResponse<bool> ToExists(SqlResponse<int> response)
            => response.State == ResponseState.Valid ? new SqlResponse<bool>(response.Result != 0) : new SqlResponse<bool>(response.Error);

        private static bool NeedsCount(Core.SelectQuery query)
            => query.PagingControls.Top > 0 || query.PagingControls.Limit > 0 || query.PagingControls.Offset > 0;

        internal static IAsyncEnumerable<T> ExecuteAsyncStream<T>(Core.SelectQuery query, BaseConnector connector,
            DbTransaction transaction, CancellationToken cancellationToken, TimeSpan? commandTimeout = null) where T : class
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            cancellationToken.ThrowIfCancellationRequested();
            commandTimeout = connector.ResolveCommandTimeout(commandTimeout);
            return ExecuteCapturedAsyncStream<T>(connector, query.Capture(), transaction, cancellationToken, commandTimeout: commandTimeout);
        }

        private static async IAsyncEnumerable<T> ExecuteCapturedAsyncStream<T>(BaseConnector connector, QuerySnapshot snapshot,
            DbTransaction transaction, [EnumeratorCancellation] CancellationToken cancellationToken, TimeSpan? commandTimeout = null) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var row in connector.ExecuteReaderModelAsyncStream<T>(snapshot.RenderForExecution<T>(connector), transaction, cancellationToken, commandTimeout).ConfigureAwait(false))
                yield return row;
        }
    }
}
