using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;
using SqlLinqer.Compilation;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Typed
{
    public partial class InsertQuery<T>
    {
        /// <summary>Uses an existing application-owned table type for TVP inserts. Calls validate its mapped column names, order, and native types, and never create or drop it.</summary>
        /// <param name="name">The unqualified SQL type name.</param>
        /// <param name="schema">The type schema, or null to use the table schema, connector default, or dbo.</param>
        /// <returns>This insert query. SQL Server enforces the existing type's size, nullability, and numeric constraints.</returns>
        public InsertQuery<T> UseTvpType(string name, string schema = null)
        {
            ReusableTvpType.ValidateIdentifier(name, nameof(name));
            if (schema != null) ReusableTvpType.ValidateIdentifier(schema, nameof(schema));
            ExistingTvpName = name; ExistingTvpSchema = schema;
            return this;
        }

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
        /// Type provisioning commits before binding the TVP. Caller-owned transactions use an independent provisioning connection. Reusable types persist; per-execution types are cleaned up.
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
        /// Type provisioning commits before binding the TVP. Caller-owned transactions use an independent provisioning connection. Reusable types persist; per-execution types are cleaned up.
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

        private RenderedQuery PrepareTvp(BaseConnector connector, out string createType, out string dropType, out ReusableTvpType reusable)
        {
            var result = new RenderedQuery();
            try
            {
                reusable = null;
                if (connector.Options.ReuseTvpTypes || ExistingTvpName != null)
                {
                    var schema = (ExistingTvpName == null ? connector.Options.ReusableTvpSchema : ExistingTvpSchema)
                        ?? Table.Schema ?? connector.Options.DefaultSchema ?? "dbo";
                    reusable = new ReusableTvpType(schema, RenderTempType(connector.DbFlavor, ReusableTvpType.Placeholder).Create,
                        Inserts.RenderTvpSchemaQuery(connector.DbFlavor), ExistingTvpName);
                }
                var (query, typeId) = RenderTVP(result.Parameters, connector.DbFlavor, Table.Schema, reusable?.TypeName);
                result.Text = query;
                foreach (var parameter in result.Parameters.GetAll().OfType<TvpParameter>()) result.OwnTable((DataTable)parameter.Value);
                if (reusable == null) (createType, dropType) = RenderTempType(connector.DbFlavor, typeId);
                else { createType = null; dropType = null; }
                return result;
            }
            catch
            {
                foreach (var parameter in result.Parameters.GetAll().OfType<TvpParameter>()) result.OwnTable((DataTable)parameter.Value);
                result.Dispose();
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
            var tempQuery = PrepareTvp(connector, out string createType, out string dropType, out var reusable);
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
                typeCleanupRequired = reusable == null;
                if (reusable != null)
                    transaction = connector.BeginReusableTvpExecution(reusable, conn, existingTransaction, commandTimeout);
                else
                {
                    var create = connector.ExecuteTypeDdl(new RenderedQuery(createType), conn.Database, commandTimeout: commandTimeout);
                    if (create.State == ResponseState.Error) throw create.Error;
                    transaction = existingTransaction ?? conn.BeginTransaction(IsolationLevel.ReadCommitted);
                }
                response = execute(tempQuery, transaction);
                if (response.State == ResponseState.Error) throw response.Error;
                if (reusable == null)
                {
                    var drop = connector.ExecuteTypeDdl(new RenderedQuery(dropType), conn.Database, commandTimeout: commandTimeout);
                    if (drop.State == ResponseState.Error) throw drop.Error;
                }
                typeCleanupRequired = false;
                if (existingTransaction == null) transaction.Commit();
            }
            catch (Exception error)
            {
                if (reusable != null) connector.InvalidateTvpTypes();
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
                DisposeTvpResources(tempQuery, existingTransaction == null ? conn : null,
                    existingTransaction == null ? transaction : null, response);
            }
            return response;
        }

        private static void DisposeTvpResources<TResult>(RenderedQuery query, DbConnection connection, DbTransaction transaction, SqlResponse<TResult> response)
        {
            Exception cleanupError = null;
            void Dispose(IDisposable resource)
            {
                try { resource?.Dispose(); }
                catch (Exception error) { if (cleanupError == null) cleanupError = error; }
            }
            Dispose(transaction);
            Dispose(connection);
            Dispose(query);
            if (cleanupError == null) return;
            if (response.State == ResponseState.Error) response.Error.Data["TvpResourceCleanupError"] = cleanupError;
            else
            {
                Dispose(response.Result as IDisposable);
                response.SetError(cleanupError);
            }
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
        /// Type provisioning commits before binding the TVP. Caller-owned transactions use an independent provisioning connection. Reusable types persist; per-execution types are cleaned up.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction. The library does not commit, roll back, or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws. Cleanup of per-execution types ignores the cancelled token.</param>
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
        /// Type provisioning commits before binding the TVP. Caller-owned transactions use an independent provisioning connection. Reusable types persist; per-execution types are cleaned up.
        /// </summary>
        /// <param name="connector">The execution connector. It must create fresh connections to the same server as the write transaction.</param>
        /// <param name="transaction">An optional caller-owned transaction. The library does not commit, roll back, or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws. Cleanup of per-execution types ignores the cancelled token.</param>
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
            var tempQuery = PrepareTvp(connector, out string createType, out string dropType, out var reusable);
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
                typeCleanupRequired = reusable == null;
                if (reusable != null)
                    transaction = await connector.BeginReusableTvpExecutionAsync(reusable, conn, existingTransaction, cancellationToken, commandTimeout).ConfigureAwait(false);
                else
                {
                    var create = await connector.ExecuteTypeDdlAsync(new RenderedQuery(createType), conn.Database, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
                    if (create.State == ResponseState.Error) throw create.Error;
                    cancellationToken.ThrowIfCancellationRequested();
                    transaction = existingTransaction ?? conn.BeginTransaction(IsolationLevel.ReadCommitted);
                }
                response = await execute(tempQuery, transaction).ConfigureAwait(false);
                if (response.State == ResponseState.Error) throw response.Error;
                if (reusable == null)
                {
                    var drop = await connector.ExecuteTypeDdlAsync(new RenderedQuery(dropType), conn.Database, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
                    if (drop.State == ResponseState.Error) throw drop.Error;
                }
                typeCleanupRequired = false;
                cancellationToken.ThrowIfCancellationRequested();
                if (existingTransaction == null) transaction.Commit();
            }
            catch (Exception error)
            {
                if (reusable != null) connector.InvalidateTvpTypes();
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
                DisposeTvpResources(tempQuery, existingTransaction == null ? conn : null,
                    existingTransaction == null ? transaction : null, response);
            }
            return response;
        }
    }
}
