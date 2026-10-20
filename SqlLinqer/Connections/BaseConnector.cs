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
    /// Creates connections and executes commands to the database
    /// </summary>
    public abstract class BaseConnector
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
        /// Creates a new connection to the database
        /// </summary>
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

        /// <summary>
        /// A wrapper function to run each command to the database. This can be overriden to control the execution context.
        /// </summary>
        /// <param name="query">The query that will be executed.</param>
        /// <param name="action">The action to execute the query. This is passed by the another calling function on the connector.</param>
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
        /// Executes a database query that returns JSON with a structure of the passed type.
        /// </summary>
        /// <param name="query">The rendered query to perform.</param>
        /// <param name="existingTransaction">An existing database transaction with the connection property set.</param>
        /// <returns>A response containing a list of objects of type 'T' derserialized from the resulting JSON string output from the database.</returns>
        public SqlResponse<List<T>> ExecuteReaderJson<T>(RenderedQuery query, DbTransaction existingTransaction = null) where T : class
        {
            return ExecuteOnConnection(query, existingTransaction, false, command =>
            {
                using (var reader = command.ExecuteReader())
                {
                    if (JsonResultsRowByRow())
                    {
                        var rows = new List<T>();
                        while (reader.Read())
                            rows.Add(JsonSerializer.Deserialize<T>(reader[0].ToString(),
                                DbFlavor == DbFlavor.Sqlite ? SqliteJsonOptions : JsonOptions));
                        return rows;
                    }

                    var builder = new StringBuilder();
                    while (reader.Read())
                        builder.Append(reader[0].ToString());
                    string json = builder.ToString();
                    return string.IsNullOrWhiteSpace(json)
                        ? new List<T>()
                        : JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
                }
            });
        }

        /// <summary>
        /// Executes a database query and returns the results as a <see cref="DataTable"/>
        /// </summary>
        /// <param name="query">The rendered query to perform.</param>
        /// <param name="existingTransaction">An existing database transaction with the connection property set.</param>
        /// <returns>A response containing the results in a <see cref="DataTable"/>.</returns>
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

        /// <summary>
        /// Executes a database query and returns the first row, first column value of the query.
        /// </summary>
        /// <param name="query">The rendered query to perform.</param>
        /// <param name="existingTransaction">An existing database transaction with the connection property set.</param>
        /// <returns>A response contianing the first row, first column value of the query converted to type 'T'.</returns>
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
                return DbFlavor == DbFlavor.DuckDb ? DbValueConversion.ConvertTo<T>(scalarValue) : (T)Convert.ChangeType(scalarValue, typeof(T));
            });
        }

        /// <summary>
        /// Executes a database query and returns the number of affected rows.
        /// </summary>
        /// <param name="query">The rendered query to perform.</param>
        /// <param name="existingTransaction">An existing database transaction with the connection property set.</param>
        /// <returns>A response containing the number of affected rows.</returns>
        public SqlResponse<int> ExecuteNonQuery(RenderedQuery query, DbTransaction existingTransaction = null)
        {
            return ExecuteOnConnection(query, existingTransaction, true, command => command.ExecuteNonQuery());
        }

        /// <summary>
        /// Creates a new instance of a stored procedure query.
        /// </summary>
        // public Queries.Core.StoredProcedureQuery BeginStoredProcedure()
        // {
        //     return new Queries.Core.StoredProcedureQuery();
        // }

        /// <summary>
        /// Controlls if the ExecuteReaderJson function parses results row by row or as a single blob
        /// </summary>
        public virtual bool JsonResultsRowByRow()
        {
            switch (SqlDialect.Resolve(DbFlavor))
            {
                case DbFlavor.DuckDb:
                case DbFlavor.PostgreSql:
                case DbFlavor.MySql:
                case DbFlavor.Sqlite:
                    return true;
                case DbFlavor.SqlServer:
                    return false;
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {DbFlavor} is not supported by {nameof(BaseConnector)}.{nameof(JsonResultsRowByRow)}");
            }
        }
    }
}
