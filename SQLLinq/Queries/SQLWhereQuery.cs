using SqlLinqer.Relationships;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// <see cref="SQLWhereQuery{TObj}"/> is an abstract base object to maintain and construct the where statement of queries that use it.
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public abstract class SQLWhereQuery<TObj> : SQLBaseQuery<TObj> where TObj : new()
    {
        private readonly SQLWhereGroup<TObj> _whereGroup;

        /// <summary>
        /// If the query context has any where statements
        /// </summary>
        protected bool HasWhereQuery
        {
            get { return _whereGroup != null && _whereGroup.Group.FirstOrDefault() != null; }
        }

        /// <summary>
        /// Begins a query with where statements
        /// </summary>
        /// <param name="recursionLevel">The recursion level to initialize the config with.</param>
        protected SQLWhereQuery(int recursionLevel = 1)
            : base(recursionLevel)
        {
            _whereGroup = new SQLWhereGroup<TObj>(SQLGroupOp.AND);
        }

        private void GetJoins(SQLWhereGroup<TObj> whereGroup, Dictionary<string, SQLConfig> joins)
        {
            if (whereGroup == null || whereGroup.Group.Count() == 0)
                return;

            foreach (var where in whereGroup.Group)
            {
                if (where is SQLWhereGroup<TObj> group)
                {
                    GetJoins(group, joins);
                }
                else
                {
                    var clause = (SQLWhereClause<TObj>)where;
                    var member = clause.column ?? GetMemberFromExpression(clause.expression);
                    if (!joins.ContainsKey(member.Config.TableAlias))
                        joins.Add(member.Config.TableAlias, member.Config);
                }
            }
        }
        private void RenderWhere(DbCommand cmd, SQLWhereGroup<TObj> wheregroup, StringBuilder builder, uint start)
        {
            if (wheregroup == null || wheregroup.Group.Count() == 0)
                return;

            builder.Append("(");

            bool first = true;
            foreach (var where in wheregroup.Group)
            {

                switch (first)
                {
                    case false:
                        switch (wheregroup.op)
                        {
                            case SQLGroupOp.OR:
                                builder.Append(" OR ");
                                break;
                            default:
                                builder.Append(" AND ");
                                break;
                        }
                        break;
                    case true:
                        first = false;
                        break;
                }

                if (where is SQLWhereGroup<TObj> group)
                // where group recursive render
                {
                    RenderWhere(cmd, group, builder, start);
                }
                else
                // where clause render
                {
                    var clause = (SQLWhereClause<TObj>)where;
                    string placeholder = $"@W{++start}";
                    var sqlMember = clause.column ?? GetMemberFromExpression(clause.expression);
                    string col = $"{Wrap(sqlMember.Config.TableAlias)}.{Wrap(sqlMember.SQLName)}";

                    if (clause.value == null && (clause.op == SQLOp.EQ || clause.op == SQLOp.NOT))
                    {
                        switch (clause.op)
                        {
                            case SQLOp.EQ:
                                builder.Append($"({col} IS NULL)");
                                break;
                            case SQLOp.NOT:
                                builder.Append($"({col} IS NOT NULL)");
                                break;
                        }
                    }
                    else if (clause.value is IEnumerable && !(clause.value is string))
                    {
                        uint qCnt = 0;
                        var placeholders = new List<string>();
                        IEnumerable list = (clause.value as IEnumerable).Cast<object>();

                        foreach (object value in list)
                        {
                            string list_placeholder = $"{placeholder}_{++qCnt}";

                            DbParameter param = cmd.CreateParameter();
                            param.ParameterName = list_placeholder;
                            param.Value = value;

                            cmd.Parameters.Add(param);
                            placeholders.Add(list_placeholder);
                        }

                        switch (clause.op)
                        {
                            case SQLOp.NOTIN:
                                break;
                            default:
                                clause.op = SQLOp.IN;
                                break;
                        }

                        builder.Append($"({col} {clause.OpToString()} ({string.Join(",", placeholders)}))");
                    }
                    else if (clause.op == SQLOp.IN || clause.op == SQLOp.NOTIN)
                    {
                        builder.Append($"({col} {clause.OpToString()} ({placeholder}))");

                        DbParameter param = cmd.CreateParameter();
                        param.ParameterName = placeholder;
                        param.Value = clause.value;

                        cmd.Parameters.Add(param);
                    }
                    else
                    {
                        builder.Append($"({col} {clause.OpToString()} {placeholder})");

                        DbParameter param = cmd.CreateParameter();
                        param.ParameterName = placeholder;
                        param.Value = clause.value;

                        cmd.Parameters.Add(param);
                    }
                }
            }

            builder.Append(")");
        }

        /// <summary>
        /// Add a where group to the where query
        /// </summary>
        /// <param name="whereGroup">the <see cref="SQLWhereGroup{TObj}"/> to add to the query</param>
        /// <returns>The current <see cref="SQLWhereQuery{TObj}"/> object</returns>
        protected SQLWhereQuery<TObj> Where(SQLWhereGroup<TObj> whereGroup)
        {
            _whereGroup.Add(whereGroup);
            return this;
        }
        /// <summary>
        /// Add a where clause to the where query
        /// </summary>
        /// <param name="expression">A expression that points to the property/field of the class the where clause applies to</param>
        /// <param name="value">The value the where clause is evaluating against</param>
        /// <param name="op">The <see cref="SQLOp"/> operator to apply to the clause</param>
        /// <returns>The current <see cref="SQLWhereQuery{TObj}"/> object</returns>
        protected SQLWhereQuery<TObj> Where(Expression<Func<TObj, object>> expression, object value, SQLOp op = SQLOp.EQ)
        {
            _whereGroup.Add(expression, value, op);
            return this;
        }
        /// <summary>
        /// Add a where clause to the where query
        /// </summary>
        /// <param name="column">The column's <see cref="SQLMemberInfo"/></param>
        /// <param name="value">The value or to the where clause is evaluating against</param>
        /// <param name="op">The <see cref="SQLOp"/> operator to apply to the clause</param>
        /// <returns>The current <see cref="SQLWhereQuery{TObj}"/> object</returns>
        protected SQLWhereQuery<TObj> Where(SQLMemberInfo column, object value, SQLOp op = SQLOp.EQ)
        {
            _whereGroup.Add(column, value, op);
            return this;
        }
        /// <summary>
        /// Renders the where query. Adds the where command text and parameterized values to the command.
        /// </summary>
        /// <param name="cmd">The command to add the where to</param>
        protected void RenderWhere(DbCommand cmd)
        {
            if (_whereGroup == null || _whereGroup.Group.Count() == 0)
                return;

            uint start = 0;
            StringBuilder builder = new StringBuilder();
            builder.Append(" WHERE ");

            RenderWhere(cmd, _whereGroup, builder, start);

            cmd.CommandText += builder.ToString();
        }
        /// <summary>
        /// Adds to the from <see cref="Dictionary{TKey, TValue}"/> the additonal joins needed for the where query
        /// </summary>
        /// <param name="joins">A reference dictionary to track the joins</param>
        protected void GetJoins(Dictionary<string, SQLConfig> joins)
        {
            GetJoins(_whereGroup, joins);
        }
        /// <summary>
        /// Renders the joins of the query into a string which can be added to a <see cref="DbCommand"/>
        /// </summary>
        /// <param name="joins">A dictonary of joins</param>
        /// <returns>All of the <paramref name="joins"/> rendered into a string of left join statements</returns>
        protected string RenderJoins(Dictionary<string, SQLConfig> joins)
        {
            var joinList = joins
                .Select(x => x.Value)
                .OrderByDescending(x => x.RecursionLevel);

            StringBuilder builder = new StringBuilder();

            foreach (var join in joinList)
            {
                if (join.ParentRelationship == null)
                    builder.Append($"{Wrap(Config.TableName)} as {Wrap(Config.TableAlias)} ");
                else
                    builder.Append($"left join {Wrap(join.TableName)} as {Wrap(join.TableAlias)} on {Wrap(join.ParentRelationship.Left.Config.TableAlias)}.{Wrap(join.ParentRelationship.Left.SQLName)} = {Wrap(join.ParentRelationship.Right.Config.TableAlias)}.{Wrap(join.ParentRelationship.Right.SQLName)} ");
            }

            return builder.ToString();
        }
    }
}
