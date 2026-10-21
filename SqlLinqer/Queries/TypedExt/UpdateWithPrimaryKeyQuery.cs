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
using SqlLinqer.Components.Update;
using SqlLinqer.Components.Outputs;

namespace SqlLinqer.Queries.TypedExt
{
    /// <summary>
    /// Builds updates that return affected-row counts and touched primary keys. Add a WHERE condition to restrict the affected rows.
    /// MySQL modified-row output is unsupported.
    /// </summary>
    /// <typeparam name="T">The root model with a SqlPrimaryKey member.</typeparam>
    /// <typeparam name="TPk">The primary key value type.</typeparam>
    public partial class UpdateWithPrimaryKeyQuery<T, TPk>
        : UpdateQuery<T>
        , IQueryTyped
        , ITypedWhereCollection<UpdateWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryTypedOutput<UpdateWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryTypedUpdate<UpdateWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryExecutableWithPrimaryKeys<int, TPk>
        , IAsyncQueryExecutableWithPrimaryKeys<int, TPk>
        where T : class
    {
        /// <summary>
        /// Creates a update builder that captures affected primary keys. A primary-key mapping is required.
        /// This constructor does not add a WHERE filter.
        /// </summary>
        /// <param name="default_schema">The fallback schema for the cached model mapping.</param>
        public UpdateWithPrimaryKeyQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// Creates a update builder that captures affected primary keys. A primary-key mapping is required.
        /// This constructor does not add a WHERE filter.
        /// </summary>
        /// <param name="table">The root mapping. It must define a primary key.</param>
        public UpdateWithPrimaryKeyQuery(Table table) : base(table)
        {
            if (table.PrimaryKey == null)
                throw new System.FormatException($"Table '{table.Name}' has no primary key.");
            Outputs.AddOutput(new ColumnOutput(table.PrimaryKey, "Inserted"));
        }

        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Update<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value)
        {
            base.Update<TProperty>(expression, value);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Update<TProperty>(Expression<Func<T, TProperty>> expression, UpdateOperation operation)
        {
            base.Update<TProperty>(expression, operation);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Update<TProperty>(Expression<Func<T, TProperty>> expression_update_column, Expression<Func<T, TProperty>> expression_value_column)
        {
            base.Update<TProperty>(expression_update_column, expression_value_column);
            return this;
        }

        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> UpdateAuto(T obj)
        {
            base.UpdateAuto(obj);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> UpdateNonDefaults(T obj)
        {
            base.UpdateNonDefaults(obj);
            return this;
        }

        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Output<TProperty>(Expression<Func<T, TProperty>> expression)
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
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where(WhereCollection sub_group)
        {
            RootWhere.Where(sub_group);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp array_op)
        {
            RootWhere.Where<TProperty>(expression, values, array_op);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op)
        {
            RootWhere.Where(expression, subquery, op);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where<TProperty>(Expression<Func<T, TProperty>> expression_left, Expression<Func<T, TProperty>> expression_right, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression_left, expression_right, op);
            return this;
        }
        /// <inheritdoc/>
        public new UpdateWithPrimaryKeyQuery<T, TPk> Where(IWhereComponent component)
        {
            RootWhere.Where(component);
            return this;
        }

        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(DbTransaction transaction = null)
        {
            return Execute(ExecutionConnector, transaction);
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
                    response.SetResult(baseRes.Result, primaryKeys.Select(x => connector.DbFlavor == DbFlavor.Sqlite
                        ? Components.Render.SqliteValue.ConvertTo<TPk>(x) : connector.DbFlavor == DbFlavor.DuckDb
                        ? DbValueConversion.ConvertTo<TPk>(x) : (TPk)x).ToList());
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
