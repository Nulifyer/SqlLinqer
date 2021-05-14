using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Update query for a single object with primary key
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    /// <typeparam name="TKey">The value type of the primary key</typeparam>
    public sealed class SQLUpdateQuery<TObj, TKey> : SQLBaseQuery<TObj> where TObj : SqlLinqerObject<TObj>, new()
    {
        private readonly bool _ignoreDefaults;
        private readonly TObj _obj;

        internal SQLUpdateQuery(TObj obj, bool ignoreDefaults = false)
            : base(recursionLevel: 0)
        {
            AssertConfigHasPrimaryKey<TKey>();

            _obj = obj;
            _ignoreDefaults = ignoreDefaults;
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
                conn = CreateConnection();
            }
            catch (Exception err)
            {
                return new SQLResponse<long>(err);
            }

            // build command string
            DbCommand cmd = conn.CreateCommand();

            cmd.CommandText += $"UPDATE {Wrap(Config.TableName)} SET ";

            uint i = 0;
            StringBuilder builder = new StringBuilder();
            var columns = new List<SQLMemberInfo>();
            columns.AddRange(Config.Columns);
            foreach (SQLMemberInfo column in columns)
            {
                var value = column.GetValue(_obj);
                if (!_ignoreDefaults || value != default)
                {
                    string placeholder = $"@UC{++i}";
                    builder.Append($"{(i > 1 ? "," : null)}{Wrap(column.SQLName)} = {placeholder}");

                    DbParameter param = cmd.CreateParameter();
                    param.ParameterName = placeholder;
                    param.Value = value;

                    cmd.Parameters.Add(param);
                }
            }
            cmd.CommandText += builder.ToString();

            cmd.CommandText += $" WHERE {Wrap(Config.PrimaryKey.SQLName)} = @PKV";

            DbParameter pkv = cmd.CreateParameter();
            pkv.ParameterName = "@PKV";
            pkv.Value = Config.PrimaryKey.GetValue(_obj);
            cmd.Parameters.Add(pkv);

            return ExecuteNonQuery(cmd);
        }
    }
}
