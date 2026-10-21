using System;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Where;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Joins;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Builds a mutable SELECT over a mapped model, with typed columns, nested relationships, filters, ordering, and paging.
    /// </summary>
    /// <typeparam name="T">The mapped root model type.</typeparam>
    public partial class SelectQuery<T>
        : Core.SelectQuery
        , IQueryTyped
        , IQueryTypedPaged<SelectQuery<T>>
        , IQueryTypedSelect<SelectQuery<T>>
        , IQueryTypedSelect<SelectQuery<T>, T>
        , IQueryTypedOrder<SelectQuery<T>>
        , IQueryTypedOrder<SelectQuery<T>, T>
        , ITypedWhereCollection<SelectQuery<T>, T>
        , IQueryExecutable<List<T>>
        , IAsyncQueryExecutable<List<T>>
        , IQueryExecutableTotalCount<T>
        , IAsyncQueryExecutableTotalCount<T>
        where T : class
    {
        /// <summary>
        /// The root where group
        /// </summary>
        protected readonly new TableWhereCollection<T> RootWhere;

        /// <summary>
        /// Creates a SELECT builder for the supplied or cached model mapping.
        /// </summary>
        /// <param name="default_schema">The fallback schema for models without an explicit SqlTable schema. This does not bind an execution connector.</param>
        public SelectQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// Creates a SELECT builder for the supplied or cached model mapping.
        /// </summary>
        /// <param name="table">The root table mapping, whose database objects must already exist.</param>
        public SelectQuery(Table table) : base(table)
        {
            base.RootWhere = RootWhere = new TableWhereCollection<T>(table, SqlWhereOp.AND);
        }

        /// <inheritdoc/>
        public SelectQuery<T> Top(int num)
        {
            PagingControls.SetTop(num);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Page(int page, int pageSize)
        {
            PagingControls.SetPage(page, pageSize);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Limit(int limit, int offset)
        {
            PagingControls.SetLimit(limit, offset);
            return this;
        }
        /// <inheritdoc/>
        public new SelectQuery<T> Distinct(bool distinct = true)
        {
            base.Distinct = distinct;
            return this;
        }

        /// <inheritdoc/>
        public SelectQuery<T> SelectAuto(bool recursive = true)
        {
            base.SelectAuto(recursive, true);
            return this;
        }
        /// <inheritdoc/>
        public new SelectQuery<T> SelectRootColumns()
        {
            base.SelectRootColumns();
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Select(Column column)
        {
            base.SelectColumn(column);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Select(Relationship relationship)
        {
            base.SelectRelationship(relationship);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Select(IEnumerable<string> path)
        {
            var rel = Table.FindRelationshipFromPath(path);
            if (rel != null)
            {
                SelectRelationship(rel);
            }
            else
            {
                var col = Table.FindColumnFromPath(path);
                if (col == null)
                    throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);
                SelectColumn(col);
            }
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Select<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            Select(expression.GetMemberPath());
            return this;
        }

        /// <inheritdoc/>
        public new SelectQuery<T> OrderByAuto(bool recursive = true)
        {
            base.OrderByAuto(recursive);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> OrderBy(Column column, SqlDir dir)
        {
            base.OrderBy(column, dir, true, true);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> ThenBy(Column column, SqlDir dir)
        {
            base.ThenBy(column, dir, true, true);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> OrderBy<TProperty>(Expression<Func<T, TProperty>> expression, SqlDir dir)
        {
            var path = expression.GetMemberPath();
            var col = Table.FindColumnFromPath(path);
            if (col == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);

            base.OrderBy(col, dir, true, true);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> ThenBy<TProperty>(Expression<Func<T, TProperty>> expression, SqlDir dir)
        {
            var path = expression.GetMemberPath();
            var col = Table.FindColumnFromPath(path);
            if (col == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);

            base.ThenBy(col, dir, true, true);
            return this;
        }

        /// <inheritdoc/>
        public TableWhereCollection<T> NewWhere(SqlWhereOp where_op)
        {
            return RootWhere.NewWhere(where_op);
        }
        /// <summary>
        /// Adds a nested condition group while preserving its AND/OR operator. NewWhere creates a detached group until this method adds it.
        /// </summary>
        /// <param name="sub_group">The condition group to combine with the root predicates.</param>
        /// <returns>The same mutable query builder.</returns>
        public SelectQuery<T> Where(WhereCollection sub_group)
        {
            RootWhere.Where(sub_group);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp array_op)
        {
            RootWhere.Where<TProperty>(expression, values, array_op);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op)
        {
            RootWhere.Where(expression, subquery, op);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression_left, Expression<Func<T, TProperty>> expression_right, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression_left, expression_right, op);
            return this;
        }
        /// <inheritdoc/>
        public SelectQuery<T> Where(IWhereComponent component)
        {
            RootWhere.Where(component);
            return this;
        }

        /// <inheritdoc/>
        public SqlResponse<List<T>> Execute(DbTransaction transaction = null)
        {
            return Execute(ExecutionConnector, transaction);
        }
        /// <inheritdoc/>
        public SqlResponse<List<T>> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);
            var response = connector.ExecuteReaderJson<T>(rendered, transaction);

            return response;
        }

        /// <inheritdoc/>
        public SqlSelectResponse<T> ExecuteWithTotalCount(DbTransaction transaction = null)
        {
            return ExecuteWithTotalCount(ExecutionConnector, transaction);
        }
        /// <inheritdoc/>
        public SqlSelectResponse<T> ExecuteWithTotalCount(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            var snapshot = Capture();
            bool needsCount = PagingControls.Top > 0 || PagingControls.Limit > 0 || PagingControls.Offset > 0;
            var selectRes = new SqlSelectResponse<T>();

            var dataRes = connector.ExecuteReaderJson<T>(snapshot.Render(connector), transaction);
            if (dataRes.State == ResponseState.Error)
            {
                selectRes.SetError(dataRes.Error);
                return selectRes;
            }

            if (needsCount)
            {
                var count_query = snapshot.RenderForCount(connector.DbFlavor);
                var countRes = connector.ExecuteScalar<long>(count_query, transaction);
                if (countRes.State == ResponseState.Error)
                {
                    selectRes.SetError(countRes.Error);
                    return selectRes;
                }

                selectRes.SetResult(dataRes.Result, countRes.Result);
            }
            else
            {
                selectRes.SetResult(dataRes.Result, dataRes.Result.Count);
            }

            return selectRes;
        }
    }
}
