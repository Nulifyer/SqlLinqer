using System;
using System.Collections.Generic;
using System.Data.Common;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Insert query
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    /// <typeparam name="TKey">The value type of the primary key</typeparam>
    public class SQLInsertPrimaryReturnQuery<TObj, TKey> : SQLBaseQuery<TObj> where TObj : SqlLinqerObject<TObj>
    {
        private readonly TObj _obj;

        internal SQLInsertPrimaryReturnQuery(TObj obj)
            : base(recursionLevel: 0)
        {
            AssertConfigHasPrimaryKey<TKey>();

            _obj = obj;
        }

        /// <summary>
        /// Execute the query and return results
        /// </summary>
        /// <param name="connector">The <see cref="SqlLinqerConnector"/> to use. If null the <see cref="Default.Connector"/> is used instead.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> containing the primary key value of the inserted object</returns>
        public SQLResponse<TKey> Run(SqlLinqerConnector connector = null)
        {
            SetConnector(connector);

            DbConnection conn;
            try
            {
                if (_obj == default)
                    throw new ArgumentNullException("Obj", "The object you are trying to insert is null");

                conn = CreateConnection();
            }
            catch (Exception err)
            {
                return new SQLResponse<TKey>(err);
            }

            // create command
            DbCommand cmd = conn.CreateCommand();

            // parameterize data
            var cols = new List<string>();
            var vals = new List<string>();
            if (!Config.PrimaryKey.DBGenerated)
            {
                string col = Config.PrimaryKey.SQLName;
                cols.Add(Wrap(col));
                vals.Add($"@I{col}");

                DbParameter param = cmd.CreateParameter();
                param.ParameterName = $"@I{col}";
                param.Value = Config.PrimaryKey.GetValue(_obj);

                cmd.Parameters.Add(param);
            }
            foreach (var item in Config.Columns)
            {
                string col = item.SQLName;
                cols.Add(Wrap(col));
                vals.Add($"@I{col}");

                DbParameter param = cmd.CreateParameter();
                param.ParameterName = $"@I{col}";
                param.Value = item.GetValue(_obj);

                cmd.Parameters.Add(param);
            }

            // build command
            cmd.CommandText = $"INSERT INTO {Wrap(Config.TableName)} ({string.Join(",", cols)}) ";
            if (Config.PrimaryKey.DBGenerated && DBType != DBType.MYSQL)
            {
                cmd.CommandText += $"OUTPUT Inserted.{Wrap(Config.PrimaryKey.SQLName)} ";
            }
            cmd.CommandText += $"VALUES ({string.Join(",", vals)}) ";
            if (Config.PrimaryKey.DBGenerated && DBType == DBType.MYSQL)
            {
                cmd.CommandText += $"; SELECT LAST_INSERT_ID() ";
            }

            // execute command
            if (Config.PrimaryKey.DBGenerated)
            {
                var query = ExecuteScalar<TKey>(cmd);
                if (query.State == ResponseState.Valid)
                    Config.PrimaryKey.SetValue(_obj, query.Result);
                return query;
            }
            else
            {
                var query = ExecuteNonQuery(cmd);
                switch (query.State)
                {
                    case ResponseState.Valid:
                        return new SQLResponse<TKey>((TKey)Config.PrimaryKey.GetValue(_obj));
                    default:
                        return new SQLResponse<TKey>(query.Error);
                }
            }
        }
    }
}
