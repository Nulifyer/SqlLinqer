using System;
using System.Linq;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Components.Where;
using SqlLinqer.Components.Outputs;

namespace SqlLinqer.Queries.TypedExt
{
    /// <summary>
    /// A DELETE query. Based around a type with a primary key.
    /// </summary>
    /// <typeparam name="T">The type serving as the root data model</typeparam>
    /// <typeparam name="TPk">The type of the primary key</typeparam>
    public class DeleteWithPrimaryKeyQuery<T, TPk>
        : DeleteQuery<T>
        , IQueryTyped
        , ITypedWhereCollection<DeleteWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryTypedOutput<DeleteWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryExecutableWithPrimaryKeys<int, TPk>
        where T : class
    {
        /// <summary>
        /// A DELETE query. Based around a type with a primary key.
        /// </summary>
        public DeleteWithPrimaryKeyQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// A UPDATE query. Based around a type with a primary key.
        /// </summary>
        /// <param name="table">The root table of the query</param>
        public DeleteWithPrimaryKeyQuery(Table table) : base(table)
        {
            if (table.PrimaryKey == null)
                throw new System.FormatException($"Table '{table.Name}' has no primary key.");
            Outputs.AddOutput(new ColumnOutput(table.PrimaryKey, "Deleted"));
        }

        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Output<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            base.Output<TProperty>(expression);
            return this;
        }

        /// <inheritdoc/>
        public new TableWhereCollection<T> NewWhere(SqlWhereOp where_op)
        {
            return RootWhere.NewWhere(where_op);
        }
        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where(WhereCollection sub_group)
        {
            RootWhere.Where(sub_group);
            return this;
        }
        /// <inheritdoc/>
        public DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp array_op)
        {
            RootWhere.Where<TProperty>(expression, values, array_op);
            return this;
        }
        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op)
        {
            RootWhere.Where(expression, subquery, op);
            return this;
        }
        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression_left, Expression<Func<T, TProperty>> expression_right, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression_left, expression_right, op);
            return this;
        }
        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where(IWhereComponent component)
        {
            RootWhere.Where(component);
            return this;
        }

        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(DbTransaction transaction = null)
        {
            return Execute(SqlLinqer.Default.Connector, transaction);
        }
        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            var response = new SqlPrimaryKeysResponse<int, TPk>();
            var primaryKeys = new List<object>();

            var baseRes = base.ExecuteAndCapturePrimaryKeys(connector, transaction, primaryKeys);

            switch (baseRes.State)
            {
                case ResponseState.Valid:
                    response.SetResult(baseRes.Result, primaryKeys.Cast<TPk>().ToList());
                    break;
                case ResponseState.Error:
                    response.SetError(baseRes.Error);
                    break;
                default:
                    throw new System.NotSupportedException($"The {nameof(ResponseState)} {baseRes.State} is not supported");
            }

            return response;
        }
    }
}