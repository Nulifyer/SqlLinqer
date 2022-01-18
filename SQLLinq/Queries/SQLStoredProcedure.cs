using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Stored procedure query that returns one or more <typeparamref name="TObj"/>
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public sealed class SQLStoredProcedureQuery<TObj> : SQLBaseQuery<TObj> where TObj : new()
    {
        private readonly string _storedProcedureName;
        private readonly Dictionary<string, object> _parameters;

        internal SQLStoredProcedureQuery(string storedProcedureName, Dictionary<string, object> parameters = null)
            : base(recursionLevel: 0)
        {
            _storedProcedureName = storedProcedureName;
            _parameters = parameters;
        }

        /// <summary>
        /// Execute the stored procedure and return results using the <paramref name="connector"/> 
        /// </summary>
        /// <param name="connector">The <see cref="SqlLinqerConnector"/> to use. If null the <see cref="Default.Connector"/> is used instead.</param>
        public SQLSelectResponse<List<TObj>> Run(SqlLinqerConnector connector = null)
        {
            SetConnector(connector);

            DbConnection conn;
            try
            {
                conn = CreateConnection();
            }
            catch (Exception err)
            {
                return new SQLSelectResponse<List<TObj>>(err);
            }

            var vals = new List<string>();
            DbCommand cmd = conn.CreateCommand();

            if (DBType == DBType.SQLServer)
            {
                if (_parameters != null)
                {
                    foreach (var kv in _parameters)
                    {
                        string placeholder = $"@{kv.Key}_Value";
                        vals.Add($"@{kv.Key} = {placeholder}");

                        DbParameter param = cmd.CreateParameter();
                        param.ParameterName = placeholder;
                        param.Value = kv.Value;

                        cmd.Parameters.Add(param);
                    }
                }
                cmd.CommandText = $"EXEC {Wrap(_storedProcedureName)} {string.Join(",", vals)}";
            }
            else
            {
                if (_parameters != null)
                {
                    foreach (var kv in _parameters)
                    {
                        string placeholder = $"@{kv.Key}_Value";
                        vals.Add(placeholder);

                        DbParameter param = cmd.CreateParameter();
                        param.ParameterName = placeholder;
                        param.Value = kv.Value;

                        cmd.Parameters.Add(param);
                    }
                }
                cmd.CommandText = $"CALL {Wrap(_storedProcedureName)}({string.Join(",", vals)})";
            }

            var result = new SQLSelectResponse<List<TObj>>();

            SQLResponse<DataTable> response = ExecuteReader(cmd);
            if (response.State == ResponseState.Error)
                return result.SetError(response.Error);

            try
            {
                result.Result = PopulateObject(response.Result, useColumnAlias: false);
                result.TotalResults = result.Result.Count;
            }
            catch (Exception err)
            {
                result.Error = new SQLResponseException(cmd, innerException: err);
            }

            return result;
        }
    }
}
