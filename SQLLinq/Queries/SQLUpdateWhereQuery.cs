using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq.Expressions;
using System.Text;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Update query with where statements
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public class SQLUpdateWhereQuery<TObj> : SQLWhereQuery<TObj> where TObj : SqlLinqerObject<TObj>
    {
        private readonly List<(SQLMemberInfo column, object value)> _updates;

        internal SQLUpdateWhereQuery()
            : base()
        {
            _updates = new List<(SQLMemberInfo column, object value)>();
        }

        /// <summary>
        /// Update the expressed column with value
        /// </summary>
        /// <param name="expression">Expression that points to a member</param>
        /// <param name="value">the value to update the column to</param>
        /// <returns>The current <see cref="SQLUpdateWhereQuery{TObj}"/> object</returns>
        public SQLUpdateWhereQuery<TObj> Update(Expression<Func<TObj, object>> expression, object value)
        {
            _updates.Add((GetMemberFromExpression(expression), value));
            return this;
        }
        /// <summary>
        /// Add a where group to the where query
        /// </summary>
        /// <param name="whereGroup">the <see cref="SQLWhereGroup{TObj}"/> to add to the query</param>
        /// <returns>The current <see cref="SQLUpdateWhereQuery{TObj}"/> object</returns>
        public new SQLUpdateWhereQuery<TObj> Where(SQLWhereGroup<TObj> whereGroup)
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
        public new SQLUpdateWhereQuery<TObj> Where(Expression<Func<TObj, object>> expression, object value, SQLOp op = SQLOp.EQ)
        {
            base.Where(expression, value, op);
            return this;
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

            var joins = new Dictionary<string, SQLConfig>();
            GetJoins(joins);

            cmd.CommandText += $"UPDATE ";
            switch (DBType)
            {
                case DBType.MYSQL:
                    cmd.CommandText += $" {RenderJoins(joins)} SET ";
                    break;
                default:
                    cmd.CommandText += $"{Wrap(Config.TableAlias)} SET ";
                    break;
            }

            uint i = 0;
            StringBuilder builder = new StringBuilder();
            foreach (var (column, value) in _updates)
            {
                string placeholder = $"@UC{++i}";
                builder.Append($"{(i > 1 ? "," : null)}{Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)} = {placeholder}");

                DbParameter param = cmd.CreateParameter();
                param.ParameterName = placeholder;
                param.Value = value;

                cmd.Parameters.Add(param);
            }

            switch (DBType)
            {
                case DBType.MYSQL:
                    break;
                default:
                    builder.Append($" FROM {RenderJoins(joins)} ");
                    break;
            }

            cmd.CommandText += builder.ToString();

            RenderWhere(cmd);

            return ExecuteNonQuery(cmd);
        }

        private new void GetJoins(Dictionary<string, SQLConfig> joins)
        {
            if (!joins.ContainsKey(Config.TableAlias))
                joins.Add(Config.TableAlias, Config);

            foreach (var (column, _) in _updates)
            {
                if (!joins.ContainsKey(column.Config.TableAlias))
                    joins.Add(column.Config.TableAlias, column.Config);
                JoinParentConfigsRecursive(joins, column.Config.ParentRelationship);
            }

            base.GetJoins(joins);
        }
        private void JoinParentConfigsRecursive(Dictionary<string, SQLConfig> joins, SQLRelationship relationship)
        {
            if (relationship == null) return;
            if (!joins.ContainsKey(relationship.Left.Config.TableAlias))
                joins.Add(relationship.Left.Config.TableAlias, relationship.Left.Config);
            JoinParentConfigsRecursive(joins, relationship.Left.Config.ParentRelationship);
        }
    }
}
