using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq.Expressions;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Delete query with where statements
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public class SQLDeleteWhereQuery<TObj> : SQLWhereQuery<TObj> where TObj : SqlLinqerObject<TObj>
    {
        internal SQLDeleteWhereQuery(int recursionLevel = 0)
            : base(recursionLevel)
        {

        }

        private new void GetJoins(Dictionary<string, SQLConfig> joins)
        {
            base.GetJoins(joins);
        }

        /// <summary>
        /// Add a where group to the where query
        /// </summary>
        /// <param name="whereGroup">the <see cref="SQLWhereGroup{TObj}"/> to add to the query</param>
        /// <returns>The current <see cref="SQLUpdateWhereQuery{TObj}"/> object</returns>
        public new SQLDeleteWhereQuery<TObj> Where(SQLWhereGroup<TObj> whereGroup)
        {
            base.Where(whereGroup);
            return this;
        }
        /// <summary>
        /// Add a where clause to the where query
        /// </summary>
        /// <param name="expression">A expression that points to the property/field of the class the where clause applies to</param>
        /// <param name="value">The value or to the where clause is evaluating against</param>
        /// <param name="op">The <see cref="SQLOp"/> operator to apply to the clause</param>
        /// <returns>The current <see cref="SQLUpdateWhereQuery{TObj}"/> object</returns>
        public new SQLDeleteWhereQuery<TObj> Where(Expression<Func<TObj, object>> expression, object value, SQLOp op = SQLOp.EQ)
        {
            base.Where(expression, value, op);
            return this;
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

            // build command string
            DbCommand cmd = conn.CreateCommand();
            if (!HasWhereQuery)
            {
                cmd.CommandText += $"TRUNCATE TABLE {Wrap(Config.TableName)}";
            }
            else
            {
                var joins = new Dictionary<string, SQLConfig>();

                GetJoins(joins);

                cmd.CommandText += $"DELETE {Wrap(Config.TableAlias)} FROM {RenderJoins(joins)}";

                RenderWhere(cmd);
            }

            result = ExecuteNonQuery(cmd);

            return result;
        }
    }
}
