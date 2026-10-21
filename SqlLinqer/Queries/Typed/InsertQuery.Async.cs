using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Render;
using System.Data;

namespace SqlLinqer.Queries.Typed
{
    public partial class InsertQuery<T>
    {
        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<int>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<int>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAndCapturePrimaryKeysAsync(connector, transaction, null, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<int>> ExecuteTvpAsync(DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpAsync(ExecutionConnector, existingTransaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<int>> ExecuteTvpAsync(BaseConnector connector, DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpAndCapturePrimaryKeysAsync(connector, existingTransaction, null, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteWithOutputAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));
            return await ExecuteBatchesAsync<DataTable>(connector, transaction,
                async (query, tx) => await connector.ExecuteReaderDataTableAsync(query, tx, cancellationToken: cancellationToken).ConfigureAwait(false),
                (all, batch) =>
                {
                    if (all == null) return batch;
                    using (batch) all.Merge(batch, false, MissingSchemaAction.Add);
                    return all;
                }, table => table?.Dispose(), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<DataTable>> ExecuteTvpWithOutputAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpWithOutputAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<DataTable>> ExecuteTvpWithOutputAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpCoreAsync(connector, transaction,
                async (query, tx) => await connector.ExecuteReaderDataTableAsync(query, tx, cancellationToken: cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        internal async Task<SqlResponse<int>> ExecuteAndCapturePrimaryKeysAsync(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            if (this.Inserts.Count == 0)
            {
                var zero_res = new SqlResponse<int>();
                zero_res.SetResult(0);
                return zero_res;
            }

            var captured = primaryKeys == null ? null : new List<object>();
            var response = await ExecuteBatchesAsync<int>(connector, existingTransaction, async (rendered, tx) =>
            {
                if (primaryKeys == null)
                    return await connector.ExecuteNonQueryAsync(rendered, tx, cancellationToken: cancellationToken).ConfigureAwait(false);
                var rows = await connector.ExecuteReaderDataTableAsync(rendered, tx, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (rows.State == ResponseState.Error)
                    return new SqlResponse<int>(rows.Error);
                using (var table = rows.Result)
                {
                    foreach (DataRow row in table.Rows)
                        captured.Add(row[0]);
                    return new SqlResponse<int>(table.Rows.Count);
                }
            }, (all, batch) => all + batch, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.State == ResponseState.Valid && captured != null)
                primaryKeys.AddRange(captured);
            return response;
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        private async Task<SqlResponse<TResult>> ExecuteBatchesAsync<TResult>(BaseConnector connector, DbTransaction existingTransaction,
            Func<RenderedQuery, DbTransaction, Task<SqlResponse<TResult>>> execute,
            Func<TResult, TResult, TResult> merge, Action<TResult> discard = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCompilationCopy)
                return await SqlLinqer.Compilation.SnapshotGraph.Capture(this).ExecuteBatchesAsync(connector,
                    existingTransaction, execute, merge, discard, cancellationToken).ConfigureAwait(false);
            int parameterLimit = connector.Options.ParameterLimit;
            if (connector.DbFlavor == DbFlavor.SqlServer || connector.DbFlavor == DbFlavor.AzureSql)
                parameterLimit = parameterLimit > 0 ? Math.Min(parameterLimit, 2100) : 2100;
            int batchSize;
            try { batchSize = Math.Max(1, Inserts.GetBatchSize(parameterLimit)); }
            catch (Exception error) { return new SqlResponse<TResult>(error); }
            if (connector.DbFlavor == DbFlavor.SqlServer || connector.DbFlavor == DbFlavor.AzureSql)
                batchSize = Math.Min(batchSize, 1000);
            if (connector.DbFlavor == DbFlavor.Sqlite && !Inserts.HasColumns)
                batchSize = 1;

            // Preserve validation before opening a connection and existing Render overrides.
            var first = RenderBatch(connector, 0, Math.Min(batchSize, Inserts.Count));
            if (parameterLimit > 0 && first.Parameters.Count > parameterLimit)
                return new SqlResponse<TResult>(new InvalidOperationException("The insert command exceeds the configured parameter limit."));
            if (Inserts.Count <= batchSize)
                return await execute(first, existingTransaction).ConfigureAwait(false);

            DbConnection connection = null;
            DbTransaction transaction = null;
            TResult result = default;
            bool hasResult = false;
            var combined = new SqlResponse<TResult>();
            try
            {
                connection = existingTransaction == null ? connector.CreateConnection()
                    : existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
                if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                transaction = existingTransaction ?? connection.BeginTransaction(connector.DbFlavor == DbFlavor.Sqlite
                    ? IsolationLevel.Serializable : connector.DbFlavor == DbFlavor.DuckDb
                    ? IsolationLevel.Snapshot : IsolationLevel.ReadCommitted);
                for (int offset = 0; offset < Inserts.Count; offset += batchSize)
                {
                    var query = offset == 0 ? first : RenderBatch(connector, offset, Math.Min(batchSize, Inserts.Count - offset));
                    if (parameterLimit > 0 && query.Parameters.Count > parameterLimit)
                        throw new InvalidOperationException("The insert command exceeds the configured parameter limit.");
                    var response = await execute(query, transaction).ConfigureAwait(false);
                    if (response.State == ResponseState.Error) throw response.Error;
                    result = hasResult ? merge(result, response.Result) : response.Result;
                    hasResult = true;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (existingTransaction == null) transaction.Commit();
                combined.SetResult(result);
            }
            catch (Exception error)
            {
                if (existingTransaction == null && transaction != null)
                {
                    try { transaction.Rollback(); }
                    catch { }
                }
                if (hasResult)
                {
                    try { discard?.Invoke(result); }
                    catch { }
                }
                if (error is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
                combined.SetError(error);
            }
            finally
            {
                if (existingTransaction == null)
                {
                    try
                    {
                        try { transaction?.Dispose(); }
                        finally { connection?.Dispose(); }
                    }
                    catch (Exception cleanupError)
                    {
                        if (combined.State == ResponseState.Valid)
                        {
                            try { discard?.Invoke(result); }
                            catch { }
                            combined.SetError(cleanupError);
                        }
                    }
                }
            }
            return combined;
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        internal async Task<SqlResponse<int>> ExecuteTvpAndCapturePrimaryKeysAsync(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector));
            if (Inserts.Count == 0)
                return new SqlResponse<int>(0);
            return await ExecuteTvpCoreAsync(connector, existingTransaction, async (query, tx) =>
            {
                if (primaryKeys == null)
                    return await connector.ExecuteNonQueryAsync(query, tx, cancellationToken: cancellationToken).ConfigureAwait(false);
                var rows = await connector.ExecuteReaderDataTableAsync(query, tx, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (rows.State == ResponseState.Error)
                {
                    var failed = new SqlResponse<int>();
                    failed.SetError(rows.Error);
                    return failed;
                }
                using (var table = rows.Result)
                    foreach (DataRow row in table.Rows)
                        primaryKeys.Add(row[0]);
                return new SqlResponse<int>(primaryKeys.Count);
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        private async Task<SqlResponse<TResult>> ExecuteTvpCoreAsync<TResult>(BaseConnector connector, DbTransaction existingTransaction,
            Func<RenderedQuery, DbTransaction, Task<SqlResponse<TResult>>> execute, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector));

            if (!IsCompilationCopy)
                return await SqlLinqer.Compilation.SnapshotGraph.Capture(this).ExecuteTvpCoreAsync(connector,
                    existingTransaction, execute, cancellationToken).ConfigureAwait(false);
            var tempQuery = new RenderedQuery();
            var (query, tempTypeId) = RenderTVP(tempQuery.Parameters, connector.DbFlavor, Table.Schema);
            tempQuery.Text = query;
            var (createType, dropType) = RenderTempType(connector.DbFlavor, tempTypeId);
            var response = new SqlResponse<TResult>();
            DbConnection conn = null;
            DbTransaction transaction = null;
            bool typeCreated = false;
            try
            {
                conn = existingTransaction == null
                    ? connector.CreateConnection()
                    : existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                transaction = existingTransaction ?? conn.BeginTransaction(IsolationLevel.ReadCommitted);

                var create = await connector.ExecuteNonQueryAsync(new RenderedQuery(createType), transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (create.State == ResponseState.Error) throw create.Error;
                typeCreated = true;
                response = await execute(tempQuery, transaction).ConfigureAwait(false);
                if (response.State == ResponseState.Error) throw response.Error;
                var drop = await connector.ExecuteNonQueryAsync(new RenderedQuery(dropType), transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (drop.State == ResponseState.Error) throw drop.Error;
                typeCreated = false;
                cancellationToken.ThrowIfCancellationRequested();
                if (existingTransaction == null) transaction.Commit();
            }
            catch (Exception error)
            {
                if (existingTransaction == null && transaction != null)
                {
                    try { transaction.Rollback(); }
                    catch { }
                }
                else if (typeCreated)
                {
                    // A usable caller transaction can clean up immediately. A doomed one must
                    // be rolled back by its owner, which also rolls back CREATE TYPE.
                    try { await connector.ExecuteNonQueryAsync(new RenderedQuery(dropType), transaction, cancellationToken: CancellationToken.None).ConfigureAwait(false); }
                    catch { }
                }
                if (error is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
                response.SetError(error);
            }
            finally
            {
                if (existingTransaction == null)
                {
                    try { transaction?.Dispose(); }
                    finally { conn?.Dispose(); }
                }
                (tempQuery.Parameters.GetAll().OfType<TvpParameter>().FirstOrDefault()?.Value as DataTable)?.Dispose();
            }
            return response;
        }
    }
}
