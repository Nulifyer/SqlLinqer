using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Where
{
    public class TableWhereCollection<T> 
        : TableWhereCollection
        , ITypedWhereCollection<TableWhereCollection<T>, T>
    {
        public TableWhereCollection(Table table, SqlWhereOp group_op) : base(table, group_op)
        {

        }
        public TableWhereCollection(SqlWhereOp group_op) : base(Table.GetCached<T>(), group_op)
        {

        }

        /// <inheritdoc/>
        public TableWhereCollection<T> NewWhere(SqlWhereOp where_op)
        {
            return new TableWhereCollection<T>(Table, where_op);
        }
        /// <inheritdoc/>
        public TableWhereCollection<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op)
        {
            base.WhereColumnValue(expression.GetMemberPath(), null, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public TableWhereCollection<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp sub_op)
        {
            base.WhereColumnValue(expression.GetMemberPath(), null, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public TableWhereCollection<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op)
        {
            base.WhereColumnValue(expression.GetMemberPath(), value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public TableWhereCollection<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp sub_op)
        {
            base.WhereColumnValue(expression.GetMemberPath(), value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public TableWhereCollection<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp array_op)
        {
            base.WhereColumnArrayValue(expression.GetMemberPath(), values.Cast<object>(), array_op);
            return this;
        }
        /// <inheritdoc/>
        public TableWhereCollection<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op)
        {
            base.WhereColumnArrayValue(expression.GetMemberPath(), subquery, op);
            return this;
        }
        /// <inheritdoc/>
        public TableWhereCollection<T> Where<TProperty>(Expression<Func<T, TProperty>> expression_left, Expression<Func<T, TProperty>> expression_right, SqlOp op)
        {
            base.WhereTwoColumn(expression_left.GetMemberPath(), expression_right.GetMemberPath(), op);
            return this;
        }
        /// <inheritdoc/>
        public TableWhereCollection<T> Where(IWhereComponent statement)
        {
            base.AddComponent(statement);
            return this;
        }

        public new TableWhereCollection<T> WhereColumnValue(IEnumerable<string> column_path, object value, SqlOp op, SqlSubQueryOp sub_op = SqlSubQueryOp.ANY)
        {
            base.WhereColumnValue(column_path, value, op, sub_op);
            return this;
        }
        public new TableWhereCollection<T> WhereColumnArrayValue(IEnumerable<string> column_path, IEnumerable<object> values, SqlArrayOp array_op)
        {
            base.WhereColumnArrayValue(column_path, values, array_op);
            return this;
        }
        public new TableWhereCollection<T> WhereTwoColumn(IEnumerable<string> column_path_left, IEnumerable<string> column_path_right, SqlOp op)
        {
            base.WhereTwoColumn(column_path_left, column_path_right, op);
            return this;
        }
    }
}