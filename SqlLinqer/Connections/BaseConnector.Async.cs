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

        /// <summary>Intercepts asynchronous execution. Requested cancellation propagates as an exception.</summary>
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

        /// <summary>Asynchronously executes and maps JSON results, preserving the selected SQL model contract.</summary>
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

        /// <summary>Asynchronously buffers a tabular result, including provider-owned binary streams.</summary>
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

        /// <summary>Asynchronously reads the first row's first value.</summary>
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
                return DbFlavor == DbFlavor.DuckDb ? DbValueConversion.ConvertTo<T>(scalar) : (T)Convert.ChangeType(scalar, typeof(T));
            }, cancellationToken);
        }

        /// <summary>Asynchronously executes a command and returns the affected row count.</summary>
        public Task<SqlResponse<int>> ExecuteNonQueryAsync(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
            => ExecuteOnConnectionAsync(query, existingTransaction, true,
                command => command.ExecuteNonQueryAsync(cancellationToken), cancellationToken);
    }
}
