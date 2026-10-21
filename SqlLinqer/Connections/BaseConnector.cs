using System;
using System.Text;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Json;
using SqlLinqer.Exceptions;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Creates database connections, binds rendered parameters, and maps command results into response objects.
    /// Each owned execution obtains and disposes its own connection. Supplied transactions retain caller ownership of their connection, commit, rollback, and disposal.
    /// </summary>
    public abstract partial class BaseConnector
    {
        private static readonly JsonSerializerOptions JsonOptions = ModelConfigResolver.CreateOptions();
        private static readonly JsonSerializerOptions SqliteJsonOptions = CreateSqliteJsonOptions();

        private static JsonSerializerOptions CreateSqliteJsonOptions()
        {
            var options = ModelConfigResolver.CreateOptions();
            options.Converters.Add(new DatabaseHexBytesConverter());
            return options;
        }

        /// <summary>
        /// The type of Sql database
        /// </summary>
        public DbFlavor DbFlavor { get; }
        /// <summary>
        /// A set of configuration options
        /// </summary>
        public ConnectorOptions Options { get; }

        /// <summary>
        /// Creates connections and executes commands to the database
        /// </summary>
        /// <param name="dbFlavor">The type of Sql database</param>
        /// <param name="options">A set of configuration options</param>
        protected BaseConnector(DbFlavor dbFlavor, ConnectorOptions options)
        {
            DbFlavor = dbFlavor;
            Options = options ?? new ConnectorOptions();
        }

        /// <summary>
        /// Creates a fresh provider connection. A caller using it directly owns opening, closing, and disposal.
        /// </summary>
        /// <returns>A new provider connection configured for the database.</returns>
        public abstract DbConnection CreateConnection();

        private void ImportFromTempQueryData(DbCommand command, RenderedQuery queryData)
        {
            command.CommandText = queryData.Text;

            foreach (var param in queryData.Parameters.GetAll())
            {
                DbParameter dbParam = command.CreateParameter();
                dbParam.ParameterName = DbFlavor == DbFlavor.DuckDb ? param.Placehodler.TrimStart('$') : param.Placehodler;
                if (DbFlavor == DbFlavor.DuckDb && param is TvpParameter)
                    throw new NotSupportedException("DuckDB does not support table-valued parameters.");

                switch (param.Value)
                {
                    case null:
                        dbParam.Value = DBNull.Value;
                        break;
                    default:
                        dbParam.Value = DbFlavor == DbFlavor.DuckDb && param.Value is Enum
                            ? Convert.ChangeType(param.Value, Enum.GetUnderlyingType(param.Value.GetType()))
                            : param.Value;
                        break;
                }

                if (param.DbType.HasValue)
                    dbParam.DbType = param.DbType.Value;

                if (param is CallableParameter callableParameter)
                    callableParameter.Configure(dbParam);

                if (DbFlavor == DbFlavor.Sqlite && param.Value is Enum enumValue)
                    dbParam.Value = Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()));

                if (param is TvpParameter tvpParam)
                {
                    if (DbFlavor == DbFlavor.Sqlite)
                        throw new NotSupportedException("SQLite does not support table-valued parameters.");
                    var parameterType = dbParam.GetType();
                    parameterType.GetProperty("TypeName")?.SetValue(dbParam, tvpParam.TypeName);
                    var sqlDbType = parameterType.GetProperty("SqlDbType");
                    if (sqlDbType != null)
                        sqlDbType.SetValue(dbParam, Enum.Parse(sqlDbType.PropertyType, "Structured"));
                }

                command.Parameters.Add(dbParam);
            }
        }
        private void PreProcessCommand(DbCommand command)
        {
            command.CommandTimeout = Convert.ToInt32(Options.CommandTimeout.TotalSeconds);
        }

        private SqlResponse<T> ExecuteOnConnection<T>(RenderedQuery query, DbTransaction existingTransaction,
            bool transactional, Func<DbCommand, T> action)
        {
            return ExecuteCommand(query, () =>
            {
                bool ownsConnection = existingTransaction == null;
                var connection = ownsConnection
                    ? CreateConnection()
                    : existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
                DbTransaction ownedTransaction = null;

                try
                {
                    if (connection.State != ConnectionState.Open)
                        connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        ImportFromTempQueryData(command, query);
                        PreProcessCommand(command);

                        if (transactional && ownsConnection)
                            ownedTransaction = connection.BeginTransaction(DbFlavor == DbFlavor.Sqlite
                                ? IsolationLevel.Serializable : DbFlavor == DbFlavor.DuckDb
                                ? IsolationLevel.Snapshot : IsolationLevel.ReadCommitted);

                        command.Transaction = existingTransaction ?? ownedTransaction;
                        T result;
                        if (DbFlavor == DbFlavor.DuckDb)
                        {
                            using (var deadline = new CommandDeadline(command, Options.CommandTimeout))
                                result = deadline.Execute(() => action(command));
                        }
                        else
                            result = action(command);
                        ownedTransaction?.Commit();
                        return new SqlResponse<T>(result);
                    }
                }
                catch
                {
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
                    try { ownedTransaction?.Dispose(); }
                    finally
                    {
                        if (ownsConnection)
                            connection.Dispose();
                    }
                }
            });
        }

        internal SqlResponse<T> ExecuteCallable<T>(RenderedQuery query, DbTransaction transaction, Func<DbCommand, T> action)
            => ExecuteOnConnection(query, transaction, true, action);

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
        /// Executes database JSON and buffers it into typed rows using SqlLinqer's SQL model contract.
        /// The command must return JSON in the dialect's transport format.
        /// </summary>
        /// <typeparam name="T">The mapped row type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <returns>The buffered rows and execution state. An empty JSON result array produces an empty list.</returns>
        public SqlResponse<List<T>> ExecuteReaderJson<T>(RenderedQuery query, DbTransaction existingTransaction = null) where T : class
        {
            var mapper = CreateJsonMapper<T>();
            return ExecuteOnConnection(query, existingTransaction, false, command =>
            {
                using (var reader = command.ExecuteReader())
                    return mapper.Read(reader);
            });
        }

        private JsonResultMapper<T> CreateJsonMapper<T>() where T : class
        {
            var options = DbFlavor == DbFlavor.Sqlite ? SqliteJsonOptions : JsonOptions;
            return new JsonResultMapper<T>(options, JsonResultsRowByRow());
        }

        /// <summary>
        /// Executes a tabular command and buffers its results in a DataTable that the caller must dispose.
        /// DuckDB BLOB streams are copied to byte arrays before the reader closes.
        /// </summary>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <returns>The buffered table and execution state. Dispose a successful Result table after use.</returns>
        public SqlResponse<DataTable> ExecuteReaderDataTable(RenderedQuery query, DbTransaction existingTransaction = null)
        {
            return ExecuteOnConnection(query, existingTransaction, query.Transactional, command =>
            {
                var table = new DataTable();
                try
                {
                    if (DbFlavor == DbFlavor.DuckDb)
                    {
                        using (var reader = command.ExecuteReader())
                            LoadDuckDbTable(table, reader);
                        return table;
                    }

                    using (var dataSet = new DataSet { EnforceConstraints = false })
                    using (var reader = command.ExecuteReader())
                    {
                        dataSet.Tables.Add(table);
                        table.Load(reader, LoadOption.OverwriteChanges);
                        dataSet.Tables.Remove(table);
                    }
                    return table;
                }
                catch
                {
                    table.Dispose();
                    throw;
                }
            });
        }

        private static void LoadDuckDbTable(DataTable table, DbDataReader reader)
        {
            // DuckDB returns BLOB values as streams over native result memory.
            // DataTable.Load closes the reader before callers can copy those streams.
            InitializeResultColumns(table, reader);

            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] is Stream stream)
                    {
                        using (stream)
                        using (var buffer = new MemoryStream())
                        {
                            stream.CopyTo(buffer);
                            values[i] = buffer.ToArray();
                        }
                    }
                }
                table.Rows.Add(values);
            }
        }

        private static void InitializeResultColumns(DataTable table, DbDataReader reader)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string originalName = reader.GetName(i);
                if (string.IsNullOrEmpty(originalName))
                    originalName = $"Column{i + 1}";
                string name = originalName;
                int suffix = 1;
                while (table.Columns.Contains(name))
                    name = originalName + suffix++;
                var type = reader.GetFieldType(i);
                table.Columns.Add(name, typeof(Stream).IsAssignableFrom(type) ? typeof(byte[]) : type);
            }
        }

        /// <summary>
        /// Reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null.
        /// Supports compatible numeric conversions, GUIDs, and enums.
        /// </summary>
        /// <typeparam name="T">The scalar or intercepted result type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <returns>The converted scalar and execution state, with null/default for SQL NULL when T permits null.</returns>
        public SqlResponse<T> ExecuteScalar<T>(RenderedQuery query, DbTransaction existingTransaction = null)
        {
            return ExecuteOnConnection(query, existingTransaction, true, command =>
            {
                if (DbFlavor == DbFlavor.DuckDb && (typeof(T) == typeof(byte[]) || typeof(T) == typeof(object)))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        object value = reader.Read() ? reader.GetValue(0) : null;
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
                object scalarValue = command.ExecuteScalar();
                return DbValueConversion.ConvertTo<T>(scalarValue);
            });
        }

        /// <summary>
        /// Executes a command and returns its affected-row count, preserving supplied transaction ownership.
        /// Without a supplied transaction, execution commits an owned write transaction on success and attempts rollback on failure.
        /// </summary>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public SqlResponse<int> ExecuteNonQuery(RenderedQuery query, DbTransaction existingTransaction = null)
        {
            return ExecuteOnConnection(query, existingTransaction, true, command => command.ExecuteNonQuery());
        }

        // SQL Server's table-type metadata cannot be created inside the transaction
        // that uses it. Keep generated TVP type DDL separate from transactional writes.
        internal SqlResponse<int> ExecuteTypeDdl(RenderedQuery query, string databaseName)
            => ExecuteOnConnection(query, null, false, command =>
            {
                if (!string.Equals(command.Connection.Database, databaseName, StringComparison.Ordinal))
                    command.Connection.ChangeDatabase(databaseName);
                return command.ExecuteNonQuery();
            });

        /// <summary>
        /// Controls if the ExecuteReaderJson function parses results row by row or as a single blob
        /// </summary>
        public virtual bool JsonResultsRowByRow() => SqlLinqer.Compilation.SqlDialect.For(DbFlavor).JsonRows;
    }
}
