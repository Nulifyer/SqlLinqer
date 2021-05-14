using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.Data.Common;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Delete query of object with primary key
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    /// <typeparam name="TKey">The value type of the primary key</typeparam>
    public sealed class SQLDeleteQuery<TObj, TKey> : SQLWhereQuery<TObj> where TObj : SqlLinqerObject<TObj>, new()
    {
        private readonly IEnumerable<TKey> _primarykeyValues;

        internal SQLDeleteQuery(TObj obj)
            : base(recursionLevel: 0)
        {
            if (Config.PrimaryKey == null)
                throw new InvalidOperationException($"{Config.Type.FullName} does not have a primary key");

            try
            {
                _primarykeyValues = new[] { (TKey)Config.PrimaryKey.GetValue(obj) };
            }
            catch
            {
                throw new FormatException($"Primary key for {Config.Type.FullName} is not of type {typeof(TKey).FullName}");
            }
        }
        internal SQLDeleteQuery(IEnumerable<TKey> primarykeyValues)
            : base(recursionLevel: 0)
        {
            _primarykeyValues = primarykeyValues;
        }
        internal SQLDeleteQuery(TKey primarykeyValue)
            : this(new[] { primarykeyValue })
        {

        }

        /// <summary>
        /// Execute the query and return results using the <paramref name="connector"/>
        /// </summary>
        /// <param name="connector">The <see cref="SqlLinqerConnector"/> to use. If null the <see cref="Default.Connector"/> is used instead.</param>
        /// <returns>A <see cref="SQLResponse{T}"/> containing the results</returns>
        public SQLResponse<long> Run(SqlLinqerConnector connector = null)
        {
            SetConnector(connector);

            var result = new SQLResponse<long>();

            DbConnection conn;
            try
            {
                conn = CreateConnection();
            }
            catch (Exception err)
            {
                return result.SetError(err);
            }

            // construct where
            Where(Config.PrimaryKey, _primarykeyValues, SQLOp.IN);

            // build command string
            DbCommand cmd = conn.CreateCommand();

            var joins = new Dictionary<string, SQLConfig>();

            GetJoins(joins);

            cmd.CommandText += $"DELETE {Wrap(Config.TableAlias)} FROM {RenderJoins(joins)} ";

            RenderWhere(cmd);

            result = ExecuteNonQuery(cmd);

            return result;
        }
    }
}
