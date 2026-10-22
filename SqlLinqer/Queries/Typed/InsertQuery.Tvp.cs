using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Typed
{
    public partial class InsertQuery<T>
    {
        /// <inheritdoc/>
        public SqlResponse<int> ExecuteTvp(DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null)
        {
            return ExecuteTvp(ExecutionConnector, existingTransaction, commandTimeout: commandTimeout);
        }

        /// <inheritdoc/>
        public SqlResponse<int> ExecuteTvp(BaseConnector connector, DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null)
        {
            return ExecuteTvpAndCapturePrimaryKeys(connector, existingTransaction, null, commandTimeout: commandTimeout);
        }

        /// <summary>
        /// Inserts through a generated SQL Server or Azure SQL TVP and buffers the selected output columns.
        /// Type creation and cleanup use separate autocommit connections in the write database. Inserted rows keep their write transaction.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction. The library does not commit, roll back, or dispose it.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>A response containing the combined output rows as a DataTable. The caller disposes a successful table.</returns>
        public SqlResponse<DataTable> ExecuteTvpWithOutput(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            return ExecuteTvpWithOutput(ExecutionConnector, transaction, commandTimeout: commandTimeout);
        }

        /// <summary>
        /// Inserts through a generated SQL Server or Azure SQL TVP and buffers the selected output columns.
        /// Type creation and cleanup use separate autocommit connections in the write database. Inserted rows keep their write transaction.
        /// </summary>
        /// <param name="connector">The execution connector. It must create fresh connections to the same server as the write transaction.</param>
        /// <param name="transaction">An optional caller-owned transaction. The library does not commit, roll back, or dispose it.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>A response containing the combined output rows as a DataTable. The caller disposes a successful table.</returns>
        public SqlResponse<DataTable> ExecuteTvpWithOutput(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            commandTimeout = connector.ResolveCommandTimeout(commandTimeout);
            return ExecuteTvpCore(connector, transaction,
                (query, tx) => connector.ExecuteReaderDataTable(query, tx, commandTimeout: commandTimeout), commandTimeout: commandTimeout);
        }

        internal SqlResponse<int> ExecuteTvpAndCapturePrimaryKeys(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null, TimeSpan? commandTimeout = null)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            commandTimeout = connector.ResolveCommandTimeout(commandTimeout);
            if (Inserts.Count == 0)
                return new SqlResponse<int>(0);
            return ExecuteTvpCore(connector, existingTransaction, (query, tx) =>
            {
                if (primaryKeys == null)
                    return connector.ExecuteNonQuery(query, tx, commandTimeout: commandTimeout);
                var rows = connector.ExecuteReaderDataTable(query, tx, commandTimeout: commandTimeout);
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
            }, commandTimeout: commandTimeout);
        }

        private RenderedQuery PrepareTvp(BaseConnector connector, out string createType, out string dropType)
        {
            var result = new RenderedQuery();
            try
            {
                var (query, typeId) = RenderTVP(result.Parameters, connector.DbFlavor, Table.Schema);
                result.Text = query;
                (createType, dropType) = RenderTempType(connector.DbFlavor, typeId);
                return result;
            }
            catch
            {
                foreach (var parameter in result.Parameters.GetAll().OfType<TvpParameter>())
                    (parameter.Value as DataTable)?.Dispose();
                throw;
            }
        }

        private SqlResponse<TResult> ExecuteTvpCore<TResult>(BaseConnector connector, DbTransaction existingTransaction,
            Func<RenderedQuery, DbTransaction, SqlResponse<TResult>> execute, TimeSpan? commandTimeout = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector));

            if (!IsCompilationCopy)
                return SqlLinqer.Compilation.SnapshotGraph.Capture(this).ExecuteTvpCore(connector,
                    existingTransaction, execute, commandTimeout: commandTimeout);
            var tempQuery = PrepareTvp(connector, out string createType, out string dropType);
            var response = new SqlResponse<TResult>();
            DbConnection conn = null;
            DbTransaction transaction = null;
            bool typeCleanupRequired = false;
            try
            {
                conn = existingTransaction == null
                    ? connector.CreateConnection()
                    : existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
                if (conn.State != ConnectionState.Open)
                    conn.Open();

                // Type DDL must be committed before SQL Server binds the TVP. It also
                // needs independent cleanup when a caller rolls back or dooms its transaction.
                typeCleanupRequired = true;
                var create = connector.ExecuteTypeDdl(new RenderedQuery(createType), conn.Database, commandTimeout: commandTimeout);
                if (create.State == ResponseState.Error) throw create.Error;
                transaction = existingTransaction ?? conn.BeginTransaction(IsolationLevel.ReadCommitted);
                response = execute(tempQuery, transaction);
                if (response.State == ResponseState.Error) throw response.Error;
                var drop = connector.ExecuteTypeDdl(new RenderedQuery(dropType), conn.Database, commandTimeout: commandTimeout);
                if (drop.State == ResponseState.Error) throw drop.Error;
                typeCleanupRequired = false;
                if (existingTransaction == null) transaction.Commit();
            }
            catch (Exception error)
            {
                if (existingTransaction == null && transaction != null)
                {
                    try { transaction.Rollback(); }
                    catch { }
                }
                if (typeCleanupRequired)
                {
                    try
                    {
                        var cleanup = connector.ExecuteTypeDdl(new RenderedQuery(dropType), conn.Database, commandTimeout: commandTimeout);
                        if (cleanup.State == ResponseState.Error) error.Data["TvpTypeCleanupError"] = cleanup.Error;
                    }
                    catch (Exception cleanupError) { error.Data["TvpTypeCleanupError"] = cleanupError; }
                }
                try { (response.Result as IDisposable)?.Dispose(); }
                catch { }
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

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteTvpAsync(DbTransaction existingTransaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpAsync(ExecutionConnector, existingTransaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteTvpAsync(BaseConnector connector, DbTransaction existingTransaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpAndCapturePrimaryKeysAsync(connector, existingTransaction, null, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously inserts through a generated SQL Server or Azure SQL TVP and buffers the selected output columns.
        /// Type creation and cleanup use separate autocommit connections in the write database. Inserted rows keep their write transaction.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction. The library does not commit, roll back, or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws, and type cleanup ignores the cancelled token.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>A response containing the combined output rows as a DataTable. The caller disposes a successful table.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<SqlResponse<DataTable>> ExecuteTvpWithOutputAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpWithOutputAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously inserts through a generated SQL Server or Azure SQL TVP and buffers the selected output columns.
        /// Type creation and cleanup use separate autocommit connections in the write database. Inserted rows keep their write transaction.
        /// </summary>
        /// <param name="connector">The execution connector. It must create fresh connections to the same server as the write transaction.</param>
        /// <param name="transaction">An optional caller-owned transaction. The library does not commit, roll back, or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws, and type cleanup ignores the cancelled token.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>A response containing the combined output rows as a DataTable. The caller disposes a successful table.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<SqlResponse<DataTable>> ExecuteTvpWithOutputAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            commandTimeout = connector.ResolveCommandTimeout(commandTimeout);
            return await ExecuteTvpCoreAsync(connector, transaction,
                async (query, tx) => await connector.ExecuteReaderDataTableAsync(query, tx, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false), cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        internal async Task<SqlResponse<int>> ExecuteTvpAndCapturePrimaryKeysAsync(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            commandTimeout = connector.ResolveCommandTimeout(commandTimeout);
            if (Inserts.Count == 0)
                return new SqlResponse<int>(0);
            return await ExecuteTvpCoreAsync(connector, existingTransaction, async (query, tx) =>
            {
                if (primaryKeys == null)
                    return await connector.ExecuteNonQueryAsync(query, tx, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
                var rows = await connector.ExecuteReaderDataTableAsync(query, tx, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
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
            }, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        private async Task<SqlResponse<TResult>> ExecuteTvpCoreAsync<TResult>(BaseConnector connector, DbTransaction existingTransaction,
            Func<RenderedQuery, DbTransaction, Task<SqlResponse<TResult>>> execute, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector));

            if (!IsCompilationCopy)
                return await SqlLinqer.Compilation.SnapshotGraph.Capture(this).ExecuteTvpCoreAsync(connector,
                    existingTransaction, execute, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
            var tempQuery = PrepareTvp(connector, out string createType, out string dropType);
            var response = new SqlResponse<TResult>();
            DbConnection conn = null;
            DbTransaction transaction = null;
            bool typeCleanupRequired = false;
            try
            {
                conn = existingTransaction == null
                    ? connector.CreateConnection()
                    : existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Autocommit type DDL avoids SQL Server's self-deadlock on USER_TYPE metadata.
                // Mark the attempt first so cancellation after server execution still triggers cleanup.
                typeCleanupRequired = true;
                var create = await connector.ExecuteTypeDdlAsync(new RenderedQuery(createType), conn.Database, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
                if (create.State == ResponseState.Error) throw create.Error;
                cancellationToken.ThrowIfCancellationRequested();
                transaction = existingTransaction ?? conn.BeginTransaction(IsolationLevel.ReadCommitted);
                response = await execute(tempQuery, transaction).ConfigureAwait(false);
                if (response.State == ResponseState.Error) throw response.Error;
                var drop = await connector.ExecuteTypeDdlAsync(new RenderedQuery(dropType), conn.Database, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
                if (drop.State == ResponseState.Error) throw drop.Error;
                typeCleanupRequired = false;
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
                if (typeCleanupRequired)
                {
                    try
                    {
                        var cleanup = await connector.ExecuteTypeDdlAsync(new RenderedQuery(dropType), conn.Database, CancellationToken.None, commandTimeout: commandTimeout).ConfigureAwait(false);
                        if (cleanup.State == ResponseState.Error) error.Data["TvpTypeCleanupError"] = cleanup.Error;
                    }
                    catch (Exception cleanupError) { error.Data["TvpTypeCleanupError"] = cleanupError; }
                }
                try { (response.Result as IDisposable)?.Dispose(); }
                catch { }
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
