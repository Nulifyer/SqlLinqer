using System;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Update;
using System.Data;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// A UPDATE query. Based around a type.
    /// </summary>
    /// <typeparam name="T">The type serving as the root data model</typeparam>
    public class UpdateQuery<T>
        : Core.UpdateQuery
        , IQueryTyped
        , ITypedWhereCollection<UpdateQuery<T>, T>
        , IQueryTypedOutput<UpdateQuery<T>, T>
        , IQueryTypedUpdate<UpdateQuery<T>, T>
        , IQueryExecutable<int>
        , IQueryExecutableWithOutput
        where T : class
    {
        /// <summary>
        /// The root where group
        /// </summary>
        public readonly new TableWhereCollection<T> RootWhere;

        /// <summary>
        /// A UPDATE query. Based around a type.
        /// </summary>
        public UpdateQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// A UPDATE query. Based around a type.
        /// </summary>
        /// <param name="table">The root table of the query</param>
        public UpdateQuery(Table table) : base(table)
        {
            base.RootWhere = RootWhere = new TableWhereCollection<T>(table, SqlWhereOp.AND);
        }

        /// <inheritdoc/>
        public UpdateQuery<T> UpdateNonDefaults(T obj)
        {
            foreach (var col in Table.Columns.GetAll<ReflectedColumn>())
            {
                if (col.PrimaryKey)
                    continue;

                var value = col.GetValue(obj);
                if (col.AutoOptions.UpdateOnDefaultAlways || value != default)
                {
                    Updates.AddUpdate(new ColumnValueUpdate(col, value));
                }
            }

            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> UpdateAuto(T obj)
        {
            foreach (var col in Table.Columns.GetAll<ReflectedColumn>())
            {
                if (!col.AutoOptions.Update)
                    continue;

                if (col.PrimaryKey)
                    continue;

                var value = col.GetValue(obj);
                Updates.AddUpdate(new ColumnValueUpdate(col, value));
            }

            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Update<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value)
        {
            var path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(path);
            if (column == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);
            if (column.Table.UUID != Table.UUID)
                throw new FormatException($"Expression '{expression}' does not target a root column. Update queries can only update one table.");

            Updates.AddUpdate(new ColumnValueUpdate(column, value));

            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Update<TProperty>(Expression<Func<T, TProperty>> expression, UpdateOperation operation)
        {
            var path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(path);
            if (column == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);
            if (column.Table.UUID != Table.UUID)
                throw new FormatException($"Expression '{expression}' does not target a root column. Update queries can only update one table.");

            Updates.AddUpdate(new ColumnOperationUpdate(column, operation));

            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Update<TProperty>(Expression<Func<T, TProperty>> expression_update_column, Expression<Func<T, TProperty>> expression_value_column)
        {
            var update_path = expression_update_column.GetMemberPath();
            var update_column = Table.FindColumnFromPath(update_path);
            if (update_column == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, update_path);
            if (update_column.Table.UUID != Table.UUID)
                throw new FormatException($"Expression '{expression_update_column}' does not target a root column. Update queries can only update one table.");

            var value_path = expression_value_column.GetMemberPath();
            var value_column = Table.FindColumnFromPath(value_path);
            if (value_column == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, value_path);

            Updates.AddUpdate(new ColumnColumnUpdate(update_column, value_column));

            return this;
        }

        /// <inheritdoc/>
        public UpdateQuery<T> Output<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            var path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(path);
            if (column == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);
            else if (column.Table.UUID != Table.UUID) {
                throw new FormatException($"Colunn is not a root column from {Table}");
            }

            Outputs.AddOutput(new ColumnOutput(column, "Inserted"));
            return this;
        }

        /// <inheritdoc/>
        public TableWhereCollection<T> NewWhere(SqlWhereOp where_op)
        {
            return RootWhere.NewWhere(where_op);
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where(WhereCollection sub_group)
        {
            RootWhere.Where(sub_group);
            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression, value, op, SqlSubQueryOp.ANY);
            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp sub_op)
        {
            RootWhere.Where<TProperty>(expression, value, op, sub_op);
            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp array_op)
        {
            RootWhere.Where<TProperty>(expression, values, array_op);
            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op)
        {
            RootWhere.Where(expression, subquery, op);
            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where<TProperty>(Expression<Func<T, TProperty>> expression_left, Expression<Func<T, TProperty>> expression_right, SqlOp op)
        {
            RootWhere.Where<TProperty>(expression_left, expression_right, op);
            return this;
        }
        /// <inheritdoc/>
        public UpdateQuery<T> Where(IWhereComponent component)
        {
            RootWhere.Where(component);
            return this;
        }
        
        /// <inheritdoc/>
        public SqlResponse<int> Execute(DbTransaction transaction = null)
        {
            return Execute(SqlLinqer.Default.Connector, transaction);
        }
        /// <inheritdoc/>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            return ExecuteAndCapturePrimaryKeys(connector, transaction, null);
        }

        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(DbTransaction transaction = null)
        {
            return ExecuteWithOutput(SqlLinqer.Default.Connector, transaction);
        }
        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));
            var rendered = this.Render(connector);
            return connector.ExecuteReaderDataTable(rendered, transaction);
        }

        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// </summary>
        /// <param name="connector">The connector used to execute the query.</param>
        /// <param name="existingTransaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <param name="primaryKeys">If a list is passed, the updated primary keys will be captured in this list.</param>
        /// <returns>The retult of the query in a state based wrapper including the inserted primary keys.</returns>
        protected SqlResponse<int> ExecuteAndCapturePrimaryKeys(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null)
        {
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

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
                    foreach (System.Data.DataRow row in primaryKeyData.Result.Rows)
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