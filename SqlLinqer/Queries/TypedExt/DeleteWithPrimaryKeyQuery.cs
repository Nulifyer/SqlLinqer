using System;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Components.Where;
using SqlLinqer.Components.Outputs;

namespace SqlLinqer.Queries.TypedExt
{
    /// <summary>
    /// Builds deletes that return affected-row counts and deleted primary keys. Add a WHERE condition to restrict the affected rows.
    /// MySQL modified-row output is unsupported.
    /// </summary>
    /// <typeparam name="T">The root model with a SqlPrimaryKey member.</typeparam>
    /// <typeparam name="TPk">The primary key value type.</typeparam>
    public partial class DeleteWithPrimaryKeyQuery<T, TPk>
        : DeleteQuery<T>
        , IQueryTyped
        , ITypedWhereCollection<DeleteWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryTypedOutput<DeleteWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryExecutableWithPrimaryKeys<int, TPk>
        , IAsyncQueryExecutableWithPrimaryKeys<int, TPk>
        where T : class
    {
        /// <summary>
        /// Creates a delete builder that captures affected primary keys. A primary-key mapping is required.
        /// This constructor does not add a WHERE filter.
        /// </summary>
        /// <param name="default_schema">The fallback schema for the cached model mapping.</param>
        public DeleteWithPrimaryKeyQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// Creates a delete builder that captures affected primary keys. A primary-key mapping is required.
        /// This constructor does not add a WHERE filter.
        /// </summary>
        /// <param name="table">The root mapping. It must define a primary key.</param>
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
        /// <summary>
        /// Adds a nested condition group while preserving its AND/OR operator. NewWhere creates a detached group until this method adds it.
        /// </summary>
        /// <param name="sub_group">The condition group to combine with the root predicates.</param>
        /// <returns>The same mutable query builder.</returns>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where(WhereCollection sub_group)
        {
            RootWhere.Where(sub_group);
            return this;
        }
        /// <inheritdoc/>
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op)
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
        public new DeleteWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op)
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
    }
}
