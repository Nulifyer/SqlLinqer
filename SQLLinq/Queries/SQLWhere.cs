using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace SqlLinqer
{
    internal interface ISQLWhere<TObj> { }

    /// <summary>
    /// A collection of <see cref="ISQLWhere{TObj}"/>
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public sealed class SQLWhereGroup<TObj> : ISQLWhere<TObj>
    {
        internal SQLGroupOp op;
        internal List<ISQLWhere<TObj>> Group { get; private set; }

        /// <summary>
        /// Create a new where group
        /// </summary>
        /// <param name="op">The group operator</param>
        public SQLWhereGroup(SQLGroupOp op = SQLGroupOp.AND)
        {
            this.op = op;
            Group = new List<ISQLWhere<TObj>>();
        }

        /// <summary>
        /// Add a <see cref="SQLWhereGroup{TObj}"/> to this group
        /// </summary>
        /// <param name="group">The group to add</param>
        /// <returns>The current <see cref="SQLWhereGroup{TObj}"/> object</returns>
        public SQLWhereGroup<TObj> Add(SQLWhereGroup<TObj> group)
        {
            if (group != null && group.Group.Count > 0)
                Group.Add(group);
            return this;
        }
        /// <summary>
        /// Adds a new where clause to the group
        /// </summary>
        /// <param name="expression">A expression that points to the property/field of the class the where clause applies to</param>
        /// <param name="value">The value or to the where clause is evaluating against</param>
        /// <param name="op">The <see cref="SQLOp"/> operator to apply to the clause</param>
        /// <returns>The current <see cref="SQLWhereGroup{TObj}"/> object</returns>
        public SQLWhereGroup<TObj> Add(Expression<Func<TObj, object>> expression, object value, SQLOp op = SQLOp.EQ)
        {
            Group.Add(new SQLWhereClause<TObj>(expression, value, op));
            return this;
        }

        internal SQLWhereGroup<TObj> Add(SQLMemberInfo column, object value, SQLOp op = SQLOp.EQ)
        {
            Group.Add(new SQLWhereClause<TObj>(column, value, op));
            return this;
        }
        internal SQLWhereGroup<TObj> Clear()
        {
            Group.Clear();
            return this;
        }
    }

    internal sealed class SQLWhereClause<TObj> : ISQLWhere<TObj>
    {
        public SQLOp op;
        public SQLMemberInfo column;
        public Expression<Func<TObj, object>> expression;
        public object value;

        public SQLWhereClause(Expression<Func<TObj, object>> expression, object value, SQLOp op = SQLOp.EQ)
        {
            this.op = op;
            this.value = value;
            this.expression = expression;
        }
        public SQLWhereClause(SQLMemberInfo column, object value, SQLOp op = SQLOp.EQ)
        {
            this.op = op;
            this.column = column;
            this.value = value;
        }

        public string OpToString()
        {
            switch (op)
            {
                case SQLOp.GT:
                    return ">";
                case SQLOp.LT:
                    return "<";
                case SQLOp.GTE:
                    return ">=";
                case SQLOp.LTE:
                    return "<=";
                case SQLOp.NOT:
                    return "!=";
                case SQLOp.LIKE:
                    return " LIKE ";
                case SQLOp.NOTLIKE:
                    return " NOT LIKE ";
                case SQLOp.IN:
                    return " IN ";
                case SQLOp.NOTIN:
                    return " NOT IN ";
                default:
                    return "=";
            }
        }
    }
}
