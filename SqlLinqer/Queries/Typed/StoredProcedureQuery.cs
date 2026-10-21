using System.Data.Common;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Calls an existing stored procedure using named inputs and loads typed JSON or a DataTable result.
    /// ExecuteJson expects database JSON; ExecuteDataTable buffers ordinary columns. SQLite and DuckDB procedures are unsupported. Use SqlLinqerCallable for typed row sets, outputs, and return codes.
    /// </summary>
    /// <typeparam name="T">The mapped root model type.</typeparam>
    public partial class StoredProcedureQuery<T>
        : Core.StoredProcedureQuery
        , IQueryTyped
        , IQueryTypedStoredProcedure<StoredProcedureQuery<T>>
        where T : class
    {
        /// <summary>
        /// Creates a legacy query for an existing procedure and its optional schema. Add named inputs before execution.
        /// </summary>
        /// <param name="name">The existing stored procedure object name.</param>
        /// <param name="schema">The procedure schema, or null to render an unqualified name.</param>
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

        /// <summary>
        /// Loads stored-procedure JSON into a list of the mapped model type.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction and open connection.</param>
        /// <returns>Typed rows and execution state; an empty JSON array produces an empty list.</returns>
        public SqlResponse<List<T>> ExecuteJson(DbTransaction transaction = null)
        {
            return ExecuteJson(ExecutionConnector, transaction);
        }
        /// <summary>
        /// Loads stored-procedure JSON into a list of the mapped model type.
        /// </summary>
        /// <param name="connector">The execution connector for this call, leaving the retained connector unchanged.</param>
        /// <param name="transaction">An optional caller-owned transaction and open connection.</param>
        /// <returns>Typed rows and execution state; an empty JSON array produces an empty list.</returns>
        public SqlResponse<List<T>> ExecuteJson(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new System.ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);
            var response = connector.ExecuteReaderJson<T>(rendered, transaction);

            return response;
        }

        /// <summary>
        /// Loads a stored-procedure tabular result into a DataTable that the caller must dispose.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction and open connection.</param>
        /// <returns>The buffered table and execution state. Dispose the table after a successful response.</returns>
        public SqlResponse<System.Data.DataTable> ExecuteDataTable(DbTransaction transaction = null)
        {
            return ExecuteDataTable(ExecutionConnector, transaction);
        }
        /// <summary>
        /// Loads a stored-procedure tabular result into a DataTable that the caller must dispose.
        /// </summary>
        /// <param name="connector">The execution connector for this call, leaving the retained connector unchanged.</param>
        /// <param name="transaction">An optional caller-owned transaction and open connection.</param>
        /// <returns>The buffered table and execution state. Dispose the table after a successful response.</returns>
        public SqlResponse<System.Data.DataTable> ExecuteDataTable(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new System.ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);
            var response = connector.ExecuteReaderDataTable(rendered, transaction);

            return response;
        }
    }
}
