using System;
using System.Collections.Generic;
using System.Data.Common;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Insert query for object with no primary key
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public sealed class SQLInsertQuery<TObj> : SQLBaseQuery<TObj> where TObj : new()
    {
        private readonly bool _ignoreDefaults;
        private readonly SqlLinqerObject<TObj> _obj;

        internal SQLInsertQuery(SqlLinqerObject<TObj> obj, bool ignoreDefaults = false)
            : base(recursionLevel: 0)
        {
            _ignoreDefaults = ignoreDefaults;
            _obj = obj;
        }

        /// <summary>
        /// Execute the query and return results
        /// </summary>
        /// <param name="connector">The <see cref="SqlLinqerConnector"/> to use. If null the <see cref="Default.Connector"/> is used instead.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> containing the results or the error</returns>
        public SQLResponse<long> Run(SqlLinqerConnector connector = null)
        {
            SetConnector(connector);

            DbConnection conn;
            try
            {
                if (_obj == null)
                    throw new ArgumentNullException("obj", "Object to be inserted cannot be null");

                conn = CreateConnection();
            }
            catch (Exception err)
            {
                return new SQLResponse<long>(err);
            }

            // create command
            DbCommand cmd = conn.CreateCommand();

            // parameterize data
            var cols = new List<string>();
            var vals = new List<string>();

            foreach (var item in Config.Columns)
            {
                var value = item.GetValue(_obj);
                if (_ignoreDefaults && value == default)
                    continue;

                string col = item.SQLName;
                cols.Add(Wrap(col));
                vals.Add($"@I{col}");

                DbParameter param = cmd.CreateParameter();
                param.ParameterName = $"@I{col}";
                param.Value = value;

                cmd.Parameters.Add(param);
            }

            // build command string
            cmd.CommandText = $"INSERT INTO {Wrap(Config.TableName)} ({string.Join(",", cols)}) VALUES ({string.Join(",", vals)})";

            // execute command
            return ExecuteNonQuery(cmd);
        }
    }
}
