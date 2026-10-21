using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Json;
using SqlLinqer.Components.Render;
using SqlLinqer.Exceptions;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        private Task<SqlResponse<T>> ExecuteOnConnectionAsync<T>(RenderedQuery query, DbTransaction existingTransaction,
            bool transactional, Func<DbCommand, Task<T>> action, CancellationToken cancellationToken)
        {
            return ExecuteCommandAsync(query, async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool ownsConnection = existingTransaction == null;
                var connection = ownsConnection ? CreateConnection()
                    : existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
                DbTransaction ownedTransaction = null;
                T materialized = default;
                bool hasResult = false;
                try
                {
                    if (connection.State != ConnectionState.Open)
                        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var command = connection.CreateCommand())
                    {
                        ImportFromTempQueryData(command, query);
                        PreProcessCommand(command);
                        // netstandard2.0 has no asynchronous transaction lifecycle contract.
                        if (transactional && ownsConnection)
                            ownedTransaction = connection.BeginTransaction(DbFlavor == DbFlavor.Sqlite
                                ? IsolationLevel.Serializable : DbFlavor == DbFlavor.DuckDb
                                ? IsolationLevel.Snapshot : IsolationLevel.ReadCommitted);
                        command.Transaction = existingTransaction ?? ownedTransaction;
                        T result;
                        if (DbFlavor == DbFlavor.DuckDb)
                        {
                            using (var deadline = new CommandDeadline(command, Options.CommandTimeout, cancellationToken))
                                result = await deadline.ExecuteAsync(() => action(command)).ConfigureAwait(false);
                        }
                        else result = await action(command).ConfigureAwait(false);
                        materialized = result;
                        hasResult = true;
                        cancellationToken.ThrowIfCancellationRequested();
                        ownedTransaction?.Commit();
                        return new SqlResponse<T>(result);
                    }
                }
                catch
                {
                    if (hasResult)
                    {
                        hasResult = false;
                        try { (materialized as IDisposable)?.Dispose(); }
                        catch { }
                    }
                    if (ownedTransaction != null)
                    {
                        try { ownedTransaction.Rollback(); }
                        catch { }
                    }
                    throw;
                }
                finally
                {
                    try
                    {
                        try { ownedTransaction?.Dispose(); }
                        finally { if (ownsConnection) connection.Dispose(); }
                    }
                    catch
                    {
                        if (hasResult)
                        {
                            try { (materialized as IDisposable)?.Dispose(); }
                            catch { }
                        }
                        throw;
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Intercepts asynchronous command execution. Requested cancellation throws instead of becoming an error response.
        /// Override both ExecuteCommand and ExecuteCommandAsync when a policy must cover both execution styles.
        /// </summary>
        /// <typeparam name="T">The scalar or intercepted result type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="action">The operation supplied by the connector. Invoke it to perform the command.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <returns>The operation's response, or a response retaining the captured execution error.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        protected virtual async Task<SqlResponse<T>> ExecuteCommandAsync<T>(RenderedQuery query,
            Func<Task<SqlResponse<T>>> action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await action().ConfigureAwait(false); }
            catch (Exception error)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    if (error is OperationCanceledException cancelled && cancelled.CancellationToken == cancellationToken) throw;
                    throw new OperationCanceledException("The database operation was cancelled.", error, cancellationToken);
                }
                return new SqlResponse<T>(new SqlResponseException(query, error));
            }
        }

        internal Task<SqlResponse<T>> ExecuteCallableAsync<T>(RenderedQuery query, DbTransaction transaction,
            Func<DbCommand, Task<T>> action, CancellationToken cancellationToken)
            => ExecuteOnConnectionAsync(query, transaction, true, action, cancellationToken);

        /// <summary>
        /// Asynchronously executes database JSON and buffers it into typed rows using the configured SQL JSON contract. Requested cancellation throws.
        /// The command must return JSON in the dialect's transport format.
        /// </summary>
        /// <typeparam name="T">The mapped row type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <returns>The buffered rows and execution state. An empty JSON result array produces an empty list.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<List<T>>> ExecuteReaderJsonAsync<T>(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default) where T : class
        {
            var mapper = CreateJsonMapper<T>();
            return ExecuteOnConnectionAsync(query, existingTransaction, false, async command =>
            {
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    return await mapper.ReadAsync(reader, cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
        }

        /// <summary>
        /// Asynchronously executes a tabular command and buffers its results in a DataTable that the caller must dispose. Requested cancellation throws.
        /// DuckDB BLOB streams are copied to byte arrays before the reader closes.
        /// </summary>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <returns>The buffered table and execution state. Dispose a successful Result table after use.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<DataTable>> ExecuteReaderDataTableAsync(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            return ExecuteOnConnectionAsync(query, existingTransaction, query.Transactional, async command =>
            {
                var table = new DataTable();
                try
                {
                    using (var dataSet = new DataSet { EnforceConstraints = false })
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        dataSet.Tables.Add(table);
                        if (DbFlavor == DbFlavor.DuckDb) InitializeResultColumns(table, reader);
                        else using (var schema = new SchemaOnlyReader(reader)) table.Load(schema, LoadOption.OverwriteChanges);
                        table.BeginLoadData();
                        try
                        {
                            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var values = new object[reader.FieldCount];
                                reader.GetValues(values);
                                for (int i = 0; i < values.Length; i++)
                                    if (values[i] is Stream stream)
                                        using (stream)
                                        using (var buffer = new MemoryStream())
                                        {
                                            await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                                            values[i] = buffer.ToArray();
                                        }
                                if (DbFlavor == DbFlavor.DuckDb) table.Rows.Add(values);
                                else table.LoadDataRow(values, LoadOption.OverwriteChanges);
                            }
                        }
                        finally { table.EndLoadData(); }
                        dataSet.Tables.Remove(table);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    return table;
                }
                catch { table.Dispose(); throw; }
            }, cancellationToken);
        }

        /// <summary>
        /// Asynchronously reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null. Requested cancellation throws.
        /// Supports compatible numeric conversions, GUIDs, and enums.
        /// </summary>
        /// <typeparam name="T">The scalar or intercepted result type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <returns>The converted scalar and execution state, with null/default for SQL NULL when T permits null.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<T>> ExecuteScalarAsync<T>(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            return ExecuteOnConnectionAsync(query, existingTransaction, true, async command =>
            {
                if (DbFlavor == DbFlavor.DuckDb && (typeof(T) == typeof(byte[]) || typeof(T) == typeof(object)))
                {
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        object value = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? reader.GetValue(0) : null;
                        if (value is Stream stream)
                            using (stream)
                            using (var buffer = new MemoryStream())
                            {
                                await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                                value = buffer.ToArray();
                            }
                        return DbValueConversion.ConvertTo<T>(value);
                    }
                }
                object scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return DbValueConversion.ConvertTo<T>(scalar);
            }, cancellationToken);
        }

        /// <summary>
        /// Asynchronously executes a command and returns its affected-row count, preserving supplied transaction ownership. Requested cancellation throws.
        /// Without a supplied transaction, execution commits an owned write transaction on success and attempts rollback on failure.
        /// </summary>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<int>> ExecuteNonQueryAsync(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
            => ExecuteOnConnectionAsync(query, existingTransaction, true,
                command => command.ExecuteNonQueryAsync(cancellationToken), cancellationToken);

        internal Task<SqlResponse<int>> ExecuteTypeDdlAsync(RenderedQuery query, string databaseName, CancellationToken cancellationToken)
            => ExecuteOnConnectionAsync(query, null, false, command =>
            {
                if (!string.Equals(command.Connection.Database, databaseName, StringComparison.Ordinal))
                    command.Connection.ChangeDatabase(databaseName);
                return command.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);
    }
}
