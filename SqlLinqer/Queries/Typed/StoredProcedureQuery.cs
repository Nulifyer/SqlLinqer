using System.Data.Common;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// A stored procedure query based around a data model.
    /// </summary>
    /// <typeparam name="T">The type serving as the root data model</typeparam>
    public class StoredProcedureQuery<T> 
        : Core.StoredProcedureQuery
        , IQueryTyped
        , IQueryTypedStoredProcedure<StoredProcedureQuery<T>>
        where T : class
    {
        /// <summary>
        /// A stored procedure query based around a data model.
        /// </summary>
        /// <param name="name">The name of the stored procedure</param>
        /// <param name="schema">The schema of the stored procedure</param>
        public StoredProcedureQuery(string name, string schema = null) : base(name, schema)
        {
            
        }

        /// <inheritdoc/>
        public StoredProcedureQuery<T> AddParameter(string name, object value, System.Data.DbType? db_type = null)
        {
            Parameters.AddParameter(name, value, db_type);
            return this;
        }
        /// <inheritdoc/>
        public StoredProcedureQuery<T> AddTvpParameter(string name, System.Data.DataTable data_table, string type_name)
        {
            Parameters.AddTvpParameter(name, data_table, type_name);
            return this;
        }

        /// <inheritdoc/>
        public SqlResponse<List<T>> ExecuteJson(DbTransaction transaction = null)
        {
            return ExecuteJson(SqlLinqer.Default.Connector);
        }
        /// <inheritdoc/>
        public SqlResponse<List<T>> ExecuteJson(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new System.ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            var rendered = this.Render(connector);
            var response = connector.ExecuteReaderJson<T>(rendered, transaction);

            return response;
        }

        /// <inheritdoc/>
        public SqlResponse<System.Data.DataTable> ExecuteDataTable(DbTransaction transaction = null)
        {
            return ExecuteDataTable(SqlLinqer.Default.Connector);
        }
        /// <inheritdoc/>
        public SqlResponse<System.Data.DataTable> ExecuteDataTable(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new System.ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            var rendered = this.Render(connector);
            var response = connector.ExecuteReaderDataTable(rendered, transaction);

            return response;
        }
    }
}