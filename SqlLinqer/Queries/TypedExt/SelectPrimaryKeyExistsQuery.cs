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
using System.Runtime.CompilerServices;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Checks whether a mapped primary-key value exists without loading a model. A valid false result means no matching row exists.
    /// Check State before interpreting Result; errors also contain false.
    /// </summary>
    /// <typeparam name="T">The root model with a primary-key mapping.</typeparam>
    /// <typeparam name="TPk">The primary key value type.</typeparam>
    public partial class SelectPrimaryKeyExistsQuery<T, TPk>
        : Core.SelectQuery
        , IQueryTyped
        , IQueryExecutable<bool>
        , IAsyncQueryExecutable<bool>
        where T : class
    {
        /// <summary>
        /// Builds an existence test for one mapped primary key without selecting the model columns.
        /// </summary>
        /// <param name="primary_key_value">The key value to test.</param>
        /// <param name="default_schema">The fallback model schema when SqlTable does not specify one.</param>
        public SelectPrimaryKeyExistsQuery(TPk primary_key_value, string default_schema = null) : this(Table.GetCached<T>(default_schema), primary_key_value) { }
        /// <summary>
        /// Builds an existence test from a table mapping that defines a primary key.
        /// </summary>
        /// <param name="table">The root table mapping with a primary key.</param>
        /// <param name="primary_key_value">The key value to test.</param>
        /// <exception cref="System.FormatException">The table mapping has no primary key.</exception>
        public SelectPrimaryKeyExistsQuery(Table table, TPk primary_key_value) : base(table)
        {
            if (table.PrimaryKey == null)
                throw new System.FormatException($"Table '{table.Name}' has no primary key.");

            this.PagingControls.SetTop(1);
            this.Selects.AddStatement(new SelectFuncStatement(SqlFunc.COUNT, "Count", 1));
            this.RootWhere.AddComponent(new WhereColumnValue(Table.PrimaryKey, primary_key_value, SqlOp.EQ, SqlSubQueryOp.ANY));
        }

        /// <summary>
        /// Checks for the primary key without loading a row. Check State before interpreting a false result.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction with an open connection.</param>
        /// <returns>A valid true result for an existing key, valid false when absent, or an error response.</returns>
        public SqlResponse<bool> Execute(DbTransaction transaction = null)
        {
            return Execute(ExecutionConnector, transaction);
        }
        /// <summary>
        /// Checks for the primary key without loading a row. Check State before interpreting a false result.
        /// </summary>
        /// <param name="connector">The connector used for this execution only.</param>
        /// <param name="transaction">An optional caller-owned transaction with an open connection.</param>
        /// <returns>A valid true result for an existing key, valid false when absent, or an error response.</returns>
        public SqlResponse<bool> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var renderedQuery = new SqlLinqer.Components.Render.RenderedQuery();
            renderedQuery.Text = this.Render(renderedQuery.Parameters, connector.DbFlavor, false);
            var response = connector.ExecuteScalar<int>(renderedQuery, transaction);

            var bool_response = new SqlResponse<bool>();
            if (response.State == ResponseState.Error)
                bool_response.SetError(response.Error);
            else
                bool_response.SetResult(response.Result > 0);

            return bool_response;
        }
    }
}
