using System;
using System.Text;
using System.Data;
using System.Data.Common;
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
                dbParam.ParameterName = param.Placehodler;

                switch (param.Value)
                {
                    case null:
                        dbParam.Value = DBNull.Value;
                        break;
                    default:
                        dbParam.Value = param.Value;
                        break;
                }

                if (param.DbType.HasValue)
                    dbParam.DbType = param.DbType.Value;

                if (param is TvpParameter tvpParam)
                {
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
                            ownedTransaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

                        command.Transaction = existingTransaction ?? ownedTransaction;
                        T result = action(command);
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
                            rows.Add(JsonSerializer.Deserialize<T>(reader[0].ToString(), JsonOptions));
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
            return ExecuteOnConnection(query, existingTransaction, false, command =>
            {
                var table = new DataTable();
                try
                {
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
                object scalarValue = command.ExecuteScalar();
                return (T)Convert.ChangeType(scalarValue, typeof(T));
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
                case DbFlavor.PostgreSql:
                case DbFlavor.MySql:
                    return true;
                case DbFlavor.SqlServer:
                    return false;
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {DbFlavor} is not supported by {nameof(BaseConnector)}.{nameof(JsonResultsRowByRow)}");
            }
        }
    }
}
