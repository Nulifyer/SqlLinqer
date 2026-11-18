using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;
using SqlLinqer.Exceptions;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        private static bool RequiresScalarReader(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(byte[]) || type == typeof(object) || type == typeof(string)
                || type == typeof(DateTime) || type == typeof(DateTimeOffset);
        }
        // Chooses the connection for one execution. Only an owned connection is disposed afterwards.
        // A supplied transaction decides first, then the connection of a session connector.
        internal DbConnection ResolveConnection(DbTransaction existingTransaction, out bool ownsConnection)
        {
            ownsConnection = false;
            if (existingTransaction != null)
                return existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
            var session = SessionConnection;
            if (session == null)
            {
                ownsConnection = true;
                return CreateConnection();
            }
            if (session.State != ConnectionState.Open)
                throw new InvalidOperationException("The session connection must be open.");
            return session;
        }

        // One rule decides an owned transaction. A compiled write asks for one and a compiled read declines it.
        // A command the caller rendered by hand leaves the choice to the terminal: writes and scalars own a
        // transaction, because they can change data, and row readers do not.
        private static bool OwnsTransaction(RenderedQuery query, bool terminalDefault) => query.Transactional ?? terminalDefault;

        // The isolation level of a transaction that the library opens and completes itself.
        internal IsolationLevel OwnedIsolationLevel
            => DbFlavor == DbFlavor.Sqlite ? IsolationLevel.Serializable
                : DbFlavor == DbFlavor.DuckDb ? IsolationLevel.Snapshot : IsolationLevel.ReadCommitted;

        private SqlResponse<T> ExecuteOnConnection<T>(RenderedQuery query, DbTransaction existingTransaction,
            bool transactional, Func<DbCommand, ExecutionObservation, T> action, TimeSpan? commandTimeout = null,
            bool independent = false)
        {
            var timeout = ResolveCommandTimeout(commandTimeout);
            var observation = ExecutionObservation.Start(Options.Observer, query, DbFlavor, false);
            try
            {
                var response = ExecuteCommand(query, () =>
                {
                    // Type and lease management passes independent to stay off a session connection.
                    bool ownsConnection = independent;
                    var connection = independent ? CreateConnection()
                        : ResolveConnection(existingTransaction, out ownsConnection);
                    DbTransaction ownedTransaction = null;
                    T materialized = default;
                    bool hasResult = false;

                    try
                    {
                        if (connection.State != ConnectionState.Open)
                            connection.Open();

                        using (var command = connection.CreateCommand())
                        {
                            ImportFromTempQueryData(command, query);
                            PreProcessCommand(command, timeout);

                            // An owned transaction opens on a pooled connection and on a session connection alike.
                            if (transactional && existingTransaction == null)
                                ownedTransaction = connection.BeginTransaction(OwnedIsolationLevel);

                            command.Transaction = existingTransaction ?? ownedTransaction;
                            T result;
                            if (DbFlavor == DbFlavor.DuckDb || Options.CancelCommand != null)
                            {
                                using (var deadline = new CommandDeadline(command, timeout, cancelCommand: Options.CancelCommand, provider: DbFlavor == DbFlavor.DuckDb ? "DuckDB" : DbFlavor.ToString()))
                                    result = deadline.Execute(() => action(command, observation));
                            }
                            else result = action(command, observation);
                            materialized = result;
                            hasResult = true;
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
                            // A failed rollback must not hide the original query error.
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
                });
                observation?.Complete(response);
                return response;
            }
            catch (Exception error) { observation?.Complete(error); throw; }
        }

        /// <summary>
        /// Intercepts synchronous command execution and converts execution failures into error responses.
        /// Override both ExecuteCommand and ExecuteCommandAsync when a policy must cover both execution styles.
        /// </summary>
        /// <typeparam name="T">The scalar or intercepted result type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="action">The operation supplied by the connector. Invoke it to perform the command.</param>
        /// <returns>The operation's response, or a response retaining the captured execution error.</returns>
        protected virtual SqlResponse<T> ExecuteCommand<T>(RenderedQuery query, Func<SqlResponse<T>> action)
        {
            try
            {
                return action.Invoke();
            }
            catch (Exception error)
            {
                var response = new SqlResponse<T>();
                response.SetError(new SqlResponseException(query, error));
                return response;
            }
        }

        /// <summary>
        /// Reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null.
        /// Supports compatible numeric conversions, GUIDs, and enums.
        /// Compiler-generated SELECT commands do not open an owned write transaction. Raw commands retain their owned transaction behavior.
        /// </summary>
        /// <typeparam name="T">The scalar or intercepted result type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The converted scalar and execution state, with null/default for SQL NULL when T permits null.</returns>
        public SqlResponse<T> ExecuteScalar<T>(RenderedQuery query, DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null)
        {
            return ExecuteOnConnection(query, existingTransaction, OwnsTransaction(query, true), (command, observation) =>
            {
                if (DbFlavor == DbFlavor.DuckDb && RequiresScalarReader(typeof(T)))
                {
                    using (var reader = ReadCommand(command, observation))
                    {
                        object value = reader.Read() ? DbValueConversion.NormalizeProviderValue(reader.GetValue(0), reader, 0, DbFlavor) : null;
                        if (value is Stream stream)
                        {
                            using (stream)
                            using (var buffer = new MemoryStream())
                            {
                                stream.CopyTo(buffer);
                                value = buffer.ToArray();
                            }
                        }
                        return DbValueConversion.ConvertTo<T>(value);
                    }
                }
                object scalarValue = ScalarCommand(command, observation);
                return DbValueConversion.ConvertTo<T>(scalarValue);
            }, commandTimeout: commandTimeout);
        }

        /// <summary>
        /// Executes a command and returns its affected-row count, preserving supplied transaction ownership.
        /// Without a supplied transaction, execution commits an owned write transaction on success and attempts rollback on failure.
        /// </summary>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public SqlResponse<int> ExecuteNonQuery(RenderedQuery query, DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null)
            => ExecuteOnConnection(query, existingTransaction, OwnsTransaction(query, true), (command, observation) => WriteCommand(command, observation),
                commandTimeout: commandTimeout);

        // SQL Server's table-type metadata cannot be created inside the transaction
        // that uses it. Keep generated TVP type DDL separate from transactional writes.
        internal SqlResponse<int> ExecuteTypeDdl(RenderedQuery query, string databaseName, TimeSpan? commandTimeout = null)
            => ExecuteOnConnection(query, null, false, (command, observation) =>
            {
                if (!string.Equals(command.Connection.Database, databaseName, StringComparison.Ordinal))
                    command.Connection.ChangeDatabase(databaseName);
                return WriteCommand(command, observation);
            }, commandTimeout: commandTimeout, independent: true);

        private Task<SqlResponse<T>> ExecuteOnConnectionAsync<T>(RenderedQuery query, DbTransaction existingTransaction,
            bool transactional, Func<DbCommand, ExecutionObservation, Task<T>> action, CancellationToken cancellationToken, TimeSpan? commandTimeout = null,
            bool independent = false)
        {
            var timeout = ResolveCommandTimeout(commandTimeout);
            return Options.Observer == null
                ? ExecuteConnectionCoreAsync(query, existingTransaction, transactional, action, cancellationToken, timeout, null, independent)
                : ExecuteObservedConnectionAsync(query, existingTransaction, transactional, action, cancellationToken, timeout, independent);
        }

        private async Task<SqlResponse<T>> ExecuteObservedConnectionAsync<T>(RenderedQuery query, DbTransaction existingTransaction,
            bool transactional, Func<DbCommand, ExecutionObservation, Task<T>> action, CancellationToken cancellationToken, TimeSpan timeout,
            bool independent)
        {
            // Starting an activity inside this async method isolates Activity.Current
            // from the caller even when the provider completes synchronously.
            var observation = ExecutionObservation.Start(Options.Observer, query, DbFlavor, false);
            try
            {
                var response = await ExecuteConnectionCoreAsync(query, existingTransaction, transactional, action,
                    cancellationToken, timeout, observation, independent).ConfigureAwait(false);
                observation?.Complete(response);
                return response;
            }
            catch (Exception error) { observation?.Complete(error); throw; }
        }

        private Task<SqlResponse<T>> ExecuteConnectionCoreAsync<T>(RenderedQuery query, DbTransaction existingTransaction,
            bool transactional, Func<DbCommand, ExecutionObservation, Task<T>> action, CancellationToken cancellationToken,
            TimeSpan timeout, ExecutionObservation observation, bool independent)
        {
            return ExecuteCommandAsync(query, async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool ownsConnection = independent;
                var connection = independent ? CreateConnection()
                    : ResolveConnection(existingTransaction, out ownsConnection);
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
                        PreProcessCommand(command, timeout);
                        // netstandard2.0 has no asynchronous transaction lifecycle contract.
                        if (transactional && existingTransaction == null)
                            ownedTransaction = connection.BeginTransaction(OwnedIsolationLevel);
                        command.Transaction = existingTransaction ?? ownedTransaction;
                        T result;
                        if (DbFlavor == DbFlavor.DuckDb || Options.CancelCommand != null)
                        {
                            using (var deadline = new CommandDeadline(command, timeout, cancellationToken, Options.CancelCommand, DbFlavor == DbFlavor.DuckDb ? "DuckDB" : DbFlavor.ToString()))
                                result = await deadline.ExecuteAsync(() => action(command, observation)).ConfigureAwait(false);
                        }
                        else result = await action(command, observation).ConfigureAwait(false);
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

        /// <summary>
        /// Asynchronously reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null. Requested cancellation throws.
        /// Supports compatible numeric conversions, GUIDs, and enums.
        /// Compiler-generated SELECT commands do not open an owned write transaction. Raw commands retain their owned transaction behavior.
        /// </summary>
        /// <typeparam name="T">The scalar or intercepted result type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The converted scalar and execution state, with null/default for SQL NULL when T permits null.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<T>> ExecuteScalarAsync<T>(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            return ExecuteOnConnectionAsync(query, existingTransaction, OwnsTransaction(query, true), async (command, observation) =>
            {
                if (DbFlavor == DbFlavor.DuckDb && RequiresScalarReader(typeof(T)))
                {
                    using (var reader = await ReadCommandAsync(command, observation, cancellationToken).ConfigureAwait(false))
                    {
                        object value = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                            ? DbValueConversion.NormalizeProviderValue(reader.GetValue(0), reader, 0, DbFlavor) : null;
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
                object scalar = await ScalarCommandAsync(command, observation, cancellationToken).ConfigureAwait(false);
                return DbValueConversion.ConvertTo<T>(scalar);
            }, cancellationToken, commandTimeout: commandTimeout);
        }

        /// <summary>
        /// Asynchronously executes a command and returns its affected-row count, preserving supplied transaction ownership. Requested cancellation throws.
        /// Without a supplied transaction, execution commits an owned write transaction on success and attempts rollback on failure.
        /// </summary>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<int>> ExecuteNonQueryAsync(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteOnConnectionAsync(query, existingTransaction, OwnsTransaction(query, true),
                (command, observation) => WriteCommandAsync(command, observation, cancellationToken), cancellationToken,
                commandTimeout: commandTimeout);

        internal Task<SqlResponse<int>> ExecuteTypeDdlAsync(RenderedQuery query, string databaseName, CancellationToken cancellationToken, TimeSpan? commandTimeout = null)
            => ExecuteOnConnectionAsync(query, null, false, (command, observation) =>
            {
                if (!string.Equals(command.Connection.Database, databaseName, StringComparison.Ordinal))
                    command.Connection.ChangeDatabase(databaseName);
                return WriteCommandAsync(command, observation, cancellationToken);
            }, cancellationToken, commandTimeout: commandTimeout, independent: true);
    }
}
