using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;

namespace SqlLinqer
{
    /// <summary>
    /// Defines a database connection and other database properties used when generating queries. This class is also used to generate new connection objects.
    /// </summary>
    public class SqlLinqerConnector
    {
        private readonly Type _type;
        private readonly string _connectionString;

        /// <summary>
        /// The database parameterized value limit
        /// </summary>
        public int ParameterLimit { get; private set; }
        /// <summary>
        /// The type of sql database
        /// This changes the format of the queries so they work in the specified platform
        /// </summary>
        public DBType DBType { get; private set; }
        /// <summary>
        /// The timeout of commands sent through this connector in seconds
        /// </summary>
        public int CommandTimeout { get; private set; }

        /// <summary>
        /// Creates a new databse connector. The connector has information needed to contstruct the queries and also functions as a DBProviderFactory.
        /// </summary>
        /// <param name="connection">The connection object you use to connect to your database</param>
        /// <param name="dbType">The implementation of sql database you are connecting to</param>
        /// <param name="parameterLimit">The database's parameterized value limit</param>
        /// <param name="commandTimeout">The timeout of commands sent through this connector in seconds</param>
        public SqlLinqerConnector(DbConnection connection, DBType dbType, int parameterLimit = 2100, int connectionTimeout = 15, int commandTimeout = 15)
        {
            _type = connection.GetType();
            _connectionString = connection.ConnectionString;

            ParameterLimit = parameterLimit;
            DBType = dbType;
            CommandTimeout = commandTimeout;

            var connectionParams = _connectionString
                .Split(';')
                .Select(x => x.Split('='))
                .ToDictionary(x => x.FirstOrDefault(), x => x.LastOrDefault());

            string connTimeoutKey = "connectiontimeout";
            if (!connectionParams.ContainsKey(connTimeoutKey))
            {
                switch (dbType)
                {
                    case DBType.OracleSQL:
                        connTimeoutKey = "Connection Timeout";
                        break;
                    case DBType.PostgreSQL:
                        connTimeoutKey = "Timeout";
                        break;
                    case DBType.MYSQL:
                    case DBType.SQLServer:
                    default:
                        connTimeoutKey = "Connect Timeout";
                        break;
                }
            }

            if (connectionParams.ContainsKey(connTimeoutKey))
                connectionParams[connTimeoutKey] = connectionTimeout.ToString();
            else
                connectionParams.Add(connTimeoutKey, connectionTimeout.ToString());
            _connectionString = string.Join(";", connectionParams.Select(x => $"{x.Key}={x.Value}"));
        }

        /// <summary>
        /// Create new database connection object of the type that was passed in during contruction.
        /// </summary>
        /// <returns>A new connection object</returns>
        public DbConnection CreateConnection()
        {
            DbConnection conn = (DbConnection)Activator.CreateInstance(_type);
            conn.ConnectionString = _connectionString;
            return conn;
        }

        /// <summary>
        /// Executes all other command Executing functions as actions.
        /// Override this function to add impersonation or change the context of the execution.
        /// </summary>
        /// <typeparam name="T">The type the action returns</typeparam>
        /// <param name="action">The action to perform</param>
        /// <returns>A <see cref="SQLResponse{T}"/> containing the <typeparamref name="T"/> result</returns>
        protected virtual SQLResponse<T> ExecuteCommand<T>(Func<T> action)
        {
            try
            {
                return new SQLResponse<T>(action.Invoke());
            }
            catch (Exception error)
            {
                return new SQLResponse<T>(error);
            }
        }

        /// <summary>
        /// Executes a database command that returns rows and columns of data. Then coverts that row data into a list of <typeparamref name="T"/>.
        /// This function expects that all resulting columns are declared under <typeparamref name="T"/>. This function does not respect the realtional
        /// structure of the library. This function is intended to run custom queries that you could not through the library.
        /// </summary>
        /// <param name="command">The command to be exectuted. The command must have its connection set.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> object with a <see cref="List{T}"/> that contains the results of the query</returns>
        public SQLResponse<List<T>> ExecuteReader<T>(DbCommand command)
        {
            var query = ExecuteReader(command);
            if (query.State == ResponseState.Error)
                return new SQLResponse<List<T>>(query.Error);

            var result = new SQLResponse<List<T>>();
            try
            {
                Type type = typeof(T);
                var config = new SQLConfig(type, 0);
                var objs = new List<T>();
                foreach (DataRow row in query.Result.Rows)
                {
                    objs.Add((T)Queries.SQLBaseQuery.PopulateObject(type, config, row, false));
                }
                result.Result = objs;
            }
            catch (Exception err)
            {
                result.Error = new SQLResponseException(command.CommandText, innerException: err);
            }
            return result;
        }
        /// <summary>
        /// Executes a database command that returns rows and columns of data
        /// </summary>
        /// <param name="command">The command to be exectuted. The command must have its connection set.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> object with a <see cref="DataTable"/> that contains the results of the query</returns>
        public SQLResponse<DataTable> ExecuteReader(DbCommand command)
        {
            command.CommandTimeout = CommandTimeout;
            var response = ExecuteCommand(() =>
            {
                var result = new SQLResponse<DataTable>();

                DataTable dt = new DataTable();
                DbConnection conn = command.Connection;

                try
                {
                    conn.Open();

                    using (DataSet ds = new DataSet() { EnforceConstraints = false })
                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        ds.Tables.Add(dt);
                        dt.Load(reader, LoadOption.OverwriteChanges);
                        ds.Tables.Remove(dt);
                    }

                    result.Result = dt;

                    conn.Close();
                }
                catch (Exception err)
                {
                    result.Error = new SQLResponseException(command.CommandText, innerException: err);
                }

                return result;
            });

            switch (response.State)
            {
                case ResponseState.Valid:
                    return response.Result;
                default:
                    return new SQLResponse<DataTable>(response.Error);
            }
        }
        /// <summary>
        /// Executes a database command the returns a <typeparamref name="T"/> which is the first column of the first row
        /// This happens trough a transaction and the transaction is rolled back if there is an error thrown.
        /// </summary>
        /// <param name="command">The command to be executed. The command must have its connection set.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> that contains the first column of the first row</returns>
        public SQLResponse<T> ExecuteScalar<T>(DbCommand command)
        {
            command.CommandTimeout = CommandTimeout;
            var response = ExecuteCommand(() =>
            {
                var result = new SQLResponse<T>();

                DbConnection conn = command.Connection;
                DbTransaction transaction = null;

                try
                {
                    conn.Open();

                    transaction = conn.BeginTransaction();

                    command.Transaction = transaction;

                    var value = command.ExecuteScalar();

                    try
                    {
                        result.Result = (T)value;
                    }
                    catch (InvalidCastException)
                    {
                        TypeConverter typeConverter = TypeDescriptor.GetConverter(typeof(T));
                        result.Result = (T)typeConverter.ConvertFrom(value);
                    }

                    transaction.Commit();

                    conn.Close();
                }
                catch (Exception err)
                {
                    transaction?.Rollback();
                    result.Error = new SQLResponseException(command.CommandText, innerException: err);
                }

                return result;
            });

            switch (response.State)
            {
                case ResponseState.Valid:
                    return response.Result;
                default:
                    return new SQLResponse<T>(response.Error);
            }
        }
        /// <summary>
        /// Executes a database command the returns a <see cref="long"/> which is the number of affected rows.
        /// This happens trough a transaction and the transaction is rolled back if there is an error thrown.
        /// </summary>
        /// <param name="command">The command to be executed. The command must have its connection set.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> object with a <see cref="long"/></returns>
        public SQLResponse<long> ExecuteNonQuery(DbCommand command)
        {
            command.CommandTimeout = CommandTimeout;
            var response = ExecuteCommand(() =>
            {
                var result = new SQLResponse<long>();

                DbConnection conn = command.Connection;
                DbTransaction transaction = null;

                try
                {
                    conn.Open();

                    transaction = conn.BeginTransaction();

                    command.Transaction = transaction;

                    result.Result = command.ExecuteNonQuery();

                    transaction.Commit();

                    conn.Close();
                }
                catch (Exception err)
                {
                    transaction?.Rollback();
                    result.Error = new SQLResponseException(command.CommandText, innerException: err);
                }

                return result;
            });

            switch (response.State)
            {
                case ResponseState.Valid:
                    return response.Result;
                default:
                    return new SQLResponse<long>(response.Error);
            }

        }
        /// <summary>
        /// Executes multiple database commands with the same connection and returns the number of affected rows across all commands. 
        /// This happens trough a transaction and the transaction is rolled back if there is an error thrown.
        /// </summary>
        /// <param name="commands">The commands to be executed. The command must have its connection set and they must all be the same connection.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> object with a <see cref="long"/></returns>
        public SQLResponse<long> ExecuteNonQuery(IEnumerable<DbCommand> commands)
        {
            var response = ExecuteCommand(() =>
            {
                var result = new SQLResponse<long>();

                DbConnection conn = commands.FirstOrDefault()?.Connection;
                if (conn == null)
                    return result.SetError(new ArgumentNullException("commands", "You must provide one or more commands to process"));

                DbTransaction transaction = null;

                string currentCmd = null;
                long rows_aff = 0;

                try
                {
                    conn.Open();

                    transaction = conn.BeginTransaction();

                    foreach (DbCommand cmd in commands)
                    {
                        cmd.CommandTimeout = CommandTimeout;
                        cmd.Transaction = transaction;
                        currentCmd = cmd.CommandText;
                        rows_aff += cmd.ExecuteNonQuery();
                    }

                    transaction.Commit();

                    conn.Close();
                }
                catch (Exception err)
                {
                    transaction?.Rollback();
                    return result.SetError(new SQLResponseException(currentCmd, innerException: err));
                }

                return result.SetResult(rows_aff);
            });

            switch (response.State)
            {
                case ResponseState.Valid:
                    return response.Result;
                default:
                    return new SQLResponse<long>(response.Error);
            }
        }
    }
}
