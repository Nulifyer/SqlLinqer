using System;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Outputs;
using System.Data;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Builds a DELETE for a mapped table. Without a WHERE condition, execution deletes every root row.
    /// To remove one many-to-many link, delete its linking row using both foreign keys.
    /// </summary>
    /// <typeparam name="T">The mapped root model type.</typeparam>
    public partial class DeleteQuery<T>
        : Core.DeleteQuery
        , IQueryTyped
        , ITypedWhereCollection<DeleteQuery<T>, T>
        , IQueryTypedOutput<DeleteQuery<T>, T>
        , IQueryExecutable<int>
        , IAsyncQueryExecutable<int>
        , IQueryExecutableWithOutput
        , IAsyncQueryExecutableWithOutput
        where T : class
    {
        /// <summary>
        /// The root where group
        /// </summary>
        public readonly new TableWhereCollection<T> RootWhere;

        /// <summary>
        /// Creates a DELETE builder for the supplied or cached model mapping.
        /// </summary>
        /// <param name="default_schema">The fallback schema for models without an explicit SqlTable schema. This does not bind an execution connector.</param>
        public DeleteQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// Creates a DELETE builder for the supplied or cached model mapping.
        /// </summary>
        /// <param name="table">The root table mapping, whose database objects must already exist.</param>
        public DeleteQuery(Table table) : base(table)
        {
            base.RootWhere = RootWhere = new TableWhereCollection<T>(table, SqlWhereOp.AND);
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
        public DeleteQuery<T> Where(WhereCollection sub_group)
        {
            RootWhere.Where(sub_group);
            return this;
        }
        /// <inheritdoc/>
        public DeleteQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public DeleteQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public DeleteQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public DeleteQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public DeleteQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp array_op)
        {
            RootWhere.Where<TProperty>(expression, values, array_op);
            return this;
        }
        /// <inheritdoc/>
        public DeleteQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op)
        {
            RootWhere.Where(expression, subquery, op);
            return this;
        }
        /// <inheritdoc/>
        public DeleteQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression_left, Expression<Func<T, TProperty>> expression_right, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression_left, expression_right, op);
            return this;
        }
        /// <inheritdoc/>
        public DeleteQuery<T> Where(IWhereComponent component)
        {
            RootWhere.Where(component);
            return this;
        }

        /// <inheritdoc/>
        public DeleteQuery<T> Output<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            var path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(path);
            if (column == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);
            else if (column.Table.UUID != Table.UUID)
            {
                throw new FormatException($"Colunn is not a root column from {Table}");
            }

            Outputs.AddOutput(new ColumnOutput(column, "Deleted"));
            return this;
        }

        /// <inheritdoc/>
        public SqlResponse<int> Execute(DbTransaction transaction = null)
        {
            return Execute(ExecutionConnector, transaction);
        }
        /// <inheritdoc/>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            return ExecuteAndCapturePrimaryKeys(connector, transaction, null);
        }

        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(DbTransaction transaction = null)
        {
            return ExecuteWithOutput(ExecutionConnector, transaction);
        }
        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");
            var rendered = this.Render(connector);
            return connector.ExecuteReaderDataTable(rendered, transaction);
        }

        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// </summary>
        /// <param name="connector">The connector used to execute the query.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an open connection. The caller commits, rolls back, and disposes it.</param>
        /// <param name="primaryKeys">If a list is passed, the updated primary keys will be captured in this list.</param>
        /// <returns>The result of the query in a state based wrapper including the affected primary keys.</returns>
        protected SqlResponse<int> ExecuteAndCapturePrimaryKeys(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);

            var response = new SqlResponse<int>();
            ISqlResponse executedRes;
            if (primaryKeys == null)
            {
                var resNon = connector.ExecuteNonQuery(rendered, existingTransaction);
                if (resNon.State == ResponseState.Valid)
                    response.SetResult(resNon.Result);
                executedRes = resNon;
            }
            else
            {
                var primaryKeyData = connector.ExecuteReaderDataTable(rendered, existingTransaction);
                if (primaryKeyData.State == ResponseState.Valid)
                {
                    using (var table = primaryKeyData.Result)
                        foreach (System.Data.DataRow row in table.Rows)
                        {
                            primaryKeys.Add(row[0]);
                        }
                    response.SetResult(primaryKeys.Count);
                }
                executedRes = primaryKeyData;
            }

            if (executedRes.State == ResponseState.Error)
                response.SetError(executedRes.Error);

            return response;
        }
    }
}
