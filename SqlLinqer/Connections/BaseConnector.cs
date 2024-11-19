using System;
using System.Text;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using Newtonsoft.Json;
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
            Options = options;
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
                    dbParam
                        .GetType()
                        .GetProperty("TypeName")?
                        .SetValue(dbParam, tvpParam.TypeName);
                }

                command.Parameters.Add(dbParam);
            }
        }
        private void PreProcessCommand(DbCommand command)
        {
            command.CommandTimeout = Convert.ToInt32(Options.CommandTimeout.TotalSeconds);
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
        /// <param name="row_by_row_json">If json results are returned row by row or in one bulk dump</param>
        /// <param name="existingTransaction">An existing database transaction with the connection property set.</param>
        /// <returns>A response containing a list of objects of type 'T' derserialized from the resulting JSON string output from the database.</returns>
        public SqlResponse<List<T>> ExecuteReaderJson<T>(RenderedQuery query, DbTransaction existingTransaction = null) where T : class
        {
            return ExecuteCommand(query, () =>
            {
                var conn = existingTransaction?.Connection ?? CreateConnection();

                if (conn.State != ConnectionState.Open)
                    conn.Open();

                DbCommand command = conn.CreateCommand();
                ImportFromTempQueryData(command, query);
                PreProcessCommand(command);

                if (existingTransaction != null)
                    command.Transaction = existingTransaction;

                var response = new SqlResponse<List<T>>();
                Exception queryError = null;

                try
                {
                    bool row_by_row_json = JsonResultsRowByRow();

                    var settings = new JsonSerializerSettings()
                    {
                        MissingMemberHandling = MissingMemberHandling.Ignore,
                        ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                        ContractResolver = new ModelConfigResolver(),
                    };

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        if (row_by_row_json)
                        {
                            var rows = new List<T>();
                            while (reader.Read())
                            {
                                string single_json = reader[0].ToString();
                                var row = JsonConvert.DeserializeObject<T>(single_json, settings);
                                rows.Add(row);
                            }
                            response.SetResult(rows);
                        }
                        else
                        {
                            var builder = new StringBuilder();
                            while (reader.Read())
                            {
                                builder.Append(reader[0].ToString());
                            }
                            string json = builder.ToString();

                            builder.Clear();
                            builder = null;

                            if (string.IsNullOrWhiteSpace(json))
                            {
                                response.SetResult(new List<T>());
                            }
                            else
                            {
                                var results = JsonConvert.DeserializeObject<List<T>>(json, settings);
                                response.SetResult(results);
                            }
                        }
                    }
                }
                catch (Exception error)
                {
                    existingTransaction?.Rollback();
                    queryError = error;
                }

                if (queryError != null || existingTransaction == null)
                {
                    conn.Close();
                    conn.Dispose();
                }

                if (queryError != null)
                    throw queryError;

                return response;
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
            return ExecuteCommand(query, () =>
            {
                var conn = existingTransaction?.Connection ?? CreateConnection();

                if (conn.State != ConnectionState.Open)
                    conn.Open();

                DbCommand command = conn.CreateCommand();
                ImportFromTempQueryData(command, query);
                PreProcessCommand(command);

                if (existingTransaction != null)
                    command.Transaction = existingTransaction;

                var response = new SqlResponse<DataTable>();
                Exception queryError = null;
                DataTable dt = new DataTable();

                try
                {

                    using (DataSet ds = new DataSet() { EnforceConstraints = false })
                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        ds.Tables.Add(dt);
                        dt.Load(reader, LoadOption.OverwriteChanges);
                        ds.Tables.Remove(dt);
                    }

                    response.SetResult(dt);
                }
                catch (Exception error)
                {
                    existingTransaction?.Rollback();
                    queryError = error;
                }

                if (queryError != null || existingTransaction == null)
                {
                    conn.Close();
                    conn.Dispose();
                }

                if (queryError != null)
                    throw queryError;

                return response;
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
            return ExecuteCommand(query, () =>
            {
                var conn = existingTransaction?.Connection ?? CreateConnection();

                if (conn.State != ConnectionState.Open)
                    conn.Open();

                DbCommand command = conn.CreateCommand();
                ImportFromTempQueryData(command, query);
                PreProcessCommand(command);

                DbTransaction transaction = existingTransaction ?? conn.BeginTransaction(IsolationLevel.ReadCommitted);
                command.Transaction = transaction;

                var response = new SqlResponse<T>();
                Exception queryError = null;

                try
                {
                    object scalarValue = command.ExecuteScalar();
                    scalarValue = Convert.ChangeType(scalarValue, typeof(T));
                    response.SetResult((T)scalarValue);
                    if (existingTransaction == null)
                    {
                        transaction.Commit();
                    }
                }
                catch (Exception error)
                {
                    transaction.Rollback();
                    queryError = error;
                }

                if (queryError != null || existingTransaction == null)
                {
                    transaction.Dispose();
                    conn.Close();
                    conn.Dispose();
                }

                if (queryError != null)
                    throw queryError;

                return response;
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
            return ExecuteCommand(query, () =>
            {
                var conn = existingTransaction?.Connection ?? CreateConnection();

                if (conn.State != ConnectionState.Open)
                    conn.Open();

                DbCommand command = conn.CreateCommand();
                ImportFromTempQueryData(command, query);
                PreProcessCommand(command);

                DbTransaction transaction = existingTransaction ?? conn.BeginTransaction(IsolationLevel.ReadCommitted);
                command.Transaction = transaction;

                var response = new SqlResponse<int>();
                Exception queryError = null;

                try
                {
                    response.SetResult(command.ExecuteNonQuery());
                    if (existingTransaction == null)
                    {
                        transaction.Commit();
                    }
                }
                catch (Exception error)
                {
                    transaction.Rollback();
                    queryError = error;
                }

                if (queryError != null || existingTransaction == null)
                {
                    transaction.Dispose();
                    conn.Close();
                    conn.Dispose();
                }

                if (queryError != null)
                    throw queryError;

                return response;
            });
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
            switch (DbFlavor)
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
