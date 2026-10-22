using System;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Components.Where;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Ordering;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Executes a SQL projection into an anonymous or declared result type, including aggregate expressions and grouped totals.
    /// Where expressions use TBase; typed ordering uses projected TReturn members. Total-count execution counts projected DISTINCT rows or groups before root paging.
    /// </summary>
    /// <typeparam name="TBase">The root mapped model used in predicates.</typeparam>
    /// <typeparam name="TReturn">The result type defined by the projection.</typeparam>
    public partial class SelectAnonymousQuery<TBase, TReturn>
        : Core.SelectQuery
        , IQueryTyped
        , IQueryTypedPaged<SelectAnonymousQuery<TBase, TReturn>>
        , IQueryTypedOrder<SelectAnonymousQuery<TBase, TReturn>>
        , IQueryTypedOrder<SelectAnonymousQuery<TBase, TReturn>, TReturn>
        , ITypedWhereCollection<SelectAnonymousQuery<TBase, TReturn>, TBase>
        , IQueryExecutable<List<TReturn>>
        , IAsyncQueryExecutable<List<TReturn>>
        , IAsyncQueryExecutableStream<TReturn>
        , IQueryExecutableTotalCount<TReturn>
        , IAsyncQueryExecutableTotalCount<TReturn>
        where TBase : class
        where TReturn : class
    {
        /// <summary>
        /// The root where group
        /// </summary>
        protected readonly new TableWhereCollection<TBase> RootWhere;

        /// <summary>The table mapping for the projected result type.</summary>
        protected readonly Table ReturnTable;

        /// <summary>
        /// A SELECT query. Based around a type.
        /// </summary>
        public SelectAnonymousQuery(string default_schema = null) : this(Table.GetCached<TBase>(default_schema)) { }

        /// <summary>
        /// A SELECT query. Based around a type.
        /// </summary>
        /// <param name="table">The root table of the query</param>
        public SelectAnonymousQuery(Table table) : base(table)
        {
            base.RootWhere = RootWhere = new TableWhereCollection<TBase>(table, SqlWhereOp.AND);
            base.DisableWiths();
            ReturnTable = Table.GetCached<TReturn>();
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Top(int num)
        {
            PagingControls.SetTop(num);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Page(int page, int pageSize)
        {
            PagingControls.SetPage(page, pageSize);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Limit(int limit, int offset)
        {
            PagingControls.SetLimit(limit, offset);
            return this;
        }

        /// <inheritdoc/>
        public new SelectAnonymousQuery<TBase, TReturn> Distinct(bool distinct = true)
        {
            base.Distinct = distinct;
            return this;
        }

        /// <inheritdoc/>
        public new SelectAnonymousQuery<TBase, TReturn> OrderByAuto(bool recursive = true)
        {
            base.OrderByAuto(recursive);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> OrderBy(Column column, SqlDir dir)
        {
            base.OrderBy(column, dir, false, false);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> ThenBy(Column column, SqlDir dir)
        {
            base.ThenBy(column, dir, false, false);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> OrderBy<TProperty>(Expression<Func<TReturn, TProperty>> expression, SqlDir dir)
        {
            var path = expression.GetMemberPath();
            if (path.Count > 1)
            {
                var order = GetProjectedPathOrder(path, dir);
                OrderBys.Clear();
                OrderBys.AddStatement(order);
                return this;
            }
            var col = ReturnTable.FindColumnFromPath(path);
            if (col == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(ReturnTable, path);

            base.OrderBy(col, dir, false, false);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> ThenBy<TProperty>(Expression<Func<TReturn, TProperty>> expression, SqlDir dir)
        {
            var path = expression.GetMemberPath();
            if (path.Count > 1)
            {
                OrderBys.AddStatement(GetProjectedPathOrder(path, dir));
                return this;
            }
            var col = ReturnTable.FindColumnFromPath(path);
            if (col == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(ReturnTable, path);

            base.ThenBy(col, dir, false, false);
            return this;
        }

        private IOrderByStatement GetProjectedPathOrder(IReadOnlyList<string> path, SqlDir dir)
        {
            var relationship = Selects.TryGet(path[0]) as SelectRelationship;
            SelectStatement selected = relationship;
            for (int i = 1; i < path.Count; i++)
            {
                if (!(selected is SelectRelationship parent))
                    throw new SqlLinqer.Exceptions.SqlPathNotFoundException(ReturnTable, path);
                selected = parent.SubQuery.Selects.TryGet(path[i]);
            }

            if (relationship == null || !(selected is SelectColumn column))
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(ReturnTable, path);

            // Resolve aliases against the selected shape. A correlated MIN/MAX
            // subquery also keeps collection ordering from duplicating root rows.
            return new SubQueryOrderBy(relationship.Relationship, column.Column, dir);
        }

        /// <inheritdoc/>
        public TableWhereCollection<TBase> NewWhere(SqlWhereOp where_op)
        {
            return RootWhere.NewWhere(where_op);
        }

        /// <summary>
        /// Adds a nested condition group while preserving its AND/OR operator. NewWhere creates a detached group until this method adds it.
        /// </summary>
        /// <param name="sub_group">The condition group to combine with the root predicates.</param>
        /// <returns>The same mutable query builder.</returns>
        public SelectAnonymousQuery<TBase, TReturn> Where(WhereCollection sub_group)
        {
            RootWhere.Where(sub_group);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Where<TProperty>(Expression<Func<TBase, TProperty>> expression, SqlNull value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Where<TProperty>(Expression<Func<TBase, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Where<TProperty>(Expression<Func<TBase, TProperty>> expression, TProperty value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Where<TProperty>(Expression<Func<TBase, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Where<TProperty>(Expression<Func<TBase, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp array_op)
        {
            RootWhere.Where<TProperty>(expression, values, array_op);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Where<TProperty>(Expression<Func<TBase, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op)
        {
            RootWhere.Where(expression, subquery, op);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Where<TProperty>(Expression<Func<TBase, TProperty>> expression_left, Expression<Func<TBase, TProperty>> expression_right, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression_left, expression_right, op);
            return this;
        }

        /// <inheritdoc/>
        public SelectAnonymousQuery<TBase, TReturn> Where(IWhereComponent component)
        {
            RootWhere.Where(component);
            return this;
        }
    }
}
