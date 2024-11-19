using System;
using System.Data.Common;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// A TRUNCATE query. Based around a type.
    /// </summary>
    /// <typeparam name="T">The type serving as the root data model</typeparam>
    public class TruncateQuery<T> 
        : Core.TruncateQuery
        , IQueryTyped
        , IQueryExecutable<int>
        where T : class
    {
        
        /// <summary>
        /// A TRUNCATE query. Based around a type.
        /// </summary>
        public TruncateQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// A TRUNCATE query. Based around a type.
        /// </summary>
        /// <param name="table">The root table of the query</param>
        public TruncateQuery(Table table) : base(table) { }

        /// <inheritdoc/>
        public SqlResponse<int> Execute(DbTransaction existingTransaction = null)
        {
            return this.Execute(SqlLinqer.Default.Connector, existingTransaction);
        }
        /// <inheritdoc/>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction existingTransaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            var rendered = this.Render(connector);
            var response = connector.ExecuteNonQuery(rendered, existingTransaction);

            return response;
        }
    }
}