using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Typed
{
    public partial class InsertQuery<T>
    {
        /// <inheritdoc/>
        public SqlResponse<int> Execute(DbTransaction transaction = null)
        {
            return Execute(ExecutionConnector, transaction);
        }

        /// <inheritdoc/>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            return ExecuteAndCapturePrimaryKeys(connector, transaction, null);
        }

        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(DbTransaction transaction = null)
        {
            return ExecuteWithOutput(ExecutionConnector, transaction);
        }

        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            return ExecuteBatches<DataTable>(connector, transaction,
                (query, tx) => connector.ExecuteReaderDataTable(query, tx),
                (all, batch) =>
                {
                    if (all == null) return batch;
                    using (batch) all.Merge(batch, false, MissingSchemaAction.Add);
                    return all;
                }, table => table?.Dispose());
        }

        internal SqlResponse<int> ExecuteAndCapturePrimaryKeys(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            if (this.Inserts.Count == 0)
            {
                var zero_res = new SqlResponse<int>();
                zero_res.SetResult(0);
                return zero_res;
            }

            var captured = primaryKeys == null ? null : new List<object>();
            var response = ExecuteBatches<int>(connector, existingTransaction, (rendered, tx) =>
            {
                if (primaryKeys == null)
                    return connector.ExecuteNonQuery(rendered, tx);
                var rows = connector.ExecuteReaderDataTable(rendered, tx);
                if (rows.State == ResponseState.Error)
                    return new SqlResponse<int>(rows.Error);
                using (var table = rows.Result)
                {
                    foreach (DataRow row in table.Rows)
                        captured.Add(row[0]);
                    return new SqlResponse<int>(table.Rows.Count);
                }
            }, (all, batch) => all + batch);
            if (response.State == ResponseState.Valid && captured != null)
                primaryKeys.AddRange(captured);
            return response;
        }

        private SqlResponse<TResult> ExecuteBatches<TResult>(BaseConnector connector, DbTransaction existingTransaction,
            Func<RenderedQuery, DbTransaction, SqlResponse<TResult>> execute,
            Func<TResult, TResult, TResult> merge, Action<TResult> discard = null)
        {
            if (!IsCompilationCopy)
                return SqlLinqer.Compilation.SnapshotGraph.Capture(this).ExecuteBatches(connector,
                    existingTransaction, execute, merge, discard);
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
                return execute(first, existingTransaction);

            DbConnection connection = null;
            DbTransaction transaction = null;
            TResult result = default;
            bool hasResult = false;
            var combined = new SqlResponse<TResult>();
            try
            {
                connection = existingTransaction == null ? connector.CreateConnection()
                    : existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
                if (connection.State != ConnectionState.Open) connection.Open();
                transaction = existingTransaction ?? connection.BeginTransaction(connector.DbFlavor == DbFlavor.Sqlite
                    ? IsolationLevel.Serializable : connector.DbFlavor == DbFlavor.DuckDb
                    ? IsolationLevel.Snapshot : IsolationLevel.ReadCommitted);
                for (int offset = 0; offset < Inserts.Count; offset += batchSize)
                {
                    var query = offset == 0 ? first : RenderBatch(connector, offset, Math.Min(batchSize, Inserts.Count - offset));
                    if (parameterLimit > 0 && query.Parameters.Count > parameterLimit)
                        throw new InvalidOperationException("The insert command exceeds the configured parameter limit.");
                    var response = execute(query, transaction);
                    if (response.State == ResponseState.Error) throw response.Error;
                    result = hasResult ? merge(result, response.Result) : response.Result;
                    hasResult = true;
                }
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

        private RenderedQuery RenderBatch(BaseConnector connector, int offset, int count)
        {
            var original = Inserts;
            try
            {
                if (offset != 0 || count != original.Count)
                    Inserts = original.Slice(offset, count);
                return Render(connector);
            }
            finally { Inserts = original; }
        }

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
            return await ExecuteBatchesAsync<DataTable>(connector, transaction,
                async (query, tx) => await connector.ExecuteReaderDataTableAsync(query, tx, cancellationToken: cancellationToken).ConfigureAwait(false),
                (all, batch) =>
                {
                    if (all == null) return batch;
                    using (batch) all.Merge(batch, false, MissingSchemaAction.Add);
                    return all;
                }, table => table?.Dispose(), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        internal async Task<SqlResponse<int>> ExecuteAndCapturePrimaryKeysAsync(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

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

        /// <inheritdoc/>
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
    }
}
