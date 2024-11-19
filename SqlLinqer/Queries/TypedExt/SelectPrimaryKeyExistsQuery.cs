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
    /// A SELECT query. Based around a type.
    /// </summary>
    /// <typeparam name="T">The type serving as the root data model</typeparam>
    /// <typeparam name="TPk">The type of the primary key</typeparam>
    public class SelectPrimaryKeyExistsQuery<T, TPk>
        : Core.SelectQuery
        , IQueryTyped
        , IQueryExecutable<bool>
        where T : class
    {
        /// <summary>
        /// A SELECT query. Based around a type.
        /// </summary>
        public SelectPrimaryKeyExistsQuery(TPk primary_key_value, string default_schema = null) : this(Table.GetCached<T>(default_schema), primary_key_value) { }
        /// <summary>
        /// A SELECT query. Based around a type.
        /// </summary>
        /// <param name="table">The root table of the query</param>
        /// <param name="primary_key_value">The value of the primary key to check</param>
        public SelectPrimaryKeyExistsQuery(Table table, TPk primary_key_value) : base(table)
        {
            if (table.PrimaryKey == null)
                throw new System.FormatException($"Table '{table.Name}' has no primary key.");

            this.PagingControls.SetTop(1);
            this.Selects.AddStatement(new SelectFuncStatement(SqlFunc.COUNT, "Count", 1));
            this.RootWhere.AddComponent(new WhereColumnValue(Table.PrimaryKey, primary_key_value, SqlOp.EQ, SqlSubQueryOp.ANY));
        }

        /// <inheritdoc/>
        public SqlResponse<bool> Execute(DbTransaction transaction = null)
        {
            return Execute(SqlLinqer.Default.Connector);
        }
        /// <inheritdoc/>
        public SqlResponse<bool> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

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