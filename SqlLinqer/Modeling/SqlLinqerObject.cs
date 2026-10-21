using SqlLinqer.Components.Modeling;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Provides typed query factories for an existing mapped table. Use SqlLinqerPrimaryKeyObject for primary-key query helpers.
    /// </summary>
    /// <typeparam name="TObj">The concrete model type representing the database table.</typeparam>
    public abstract class SqlLinqerObject<TObj> where TObj : class
    {
        #region Insert
        /// <summary>
        /// Creates an empty insert builder for this mapped model.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable insert query. Add rows with InsertAuto before executing.</returns>
        public static InsertQuery<TObj> BeginInsert(BaseConnector connector = null)
        {
            return new InsertQuery<TObj>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
        }
        /// <summary>
        /// Creates an insert for this instance using automatic-insert attributes.
        /// Database-generated primary keys are omitted.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>An insert query containing this instance and its automatic-insert columns.</returns>
        public InsertQuery<TObj> BeginInsertAuto(BaseConnector connector = null)
        {
            var query = BeginInsert(connector);
            query.InsertAuto(this as TObj);
            return query;
        }
        #endregion

        #region Select
        /// <summary>
        /// Creates a SELECT builder without selecting its output columns.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable select query.</returns>
        public static SelectQuery<TObj> BeginSelect(BaseConnector connector = null)
        {
            return new SelectQuery<TObj>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
        }
        /// <summary>
        /// Creates a SELECT with automatic column and relationship selection.
        /// Relationships require SqlAutoSelect inclusion.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable select query.</returns>
        public static SelectQuery<TObj> BeginSelectAuto(BaseConnector connector = null)
        {
            var query = BeginSelect(connector);
            query.SelectAuto();
            return query;
        }
        #endregion

        #region Select Aggregate
        /// <summary>
        /// Creates an aggregate or named projection builder. BuildQuery defines the result members and SQL expressions.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>An initializer for a projection into an anonymous or declared result type.</returns>
        public static SelectQueryInitializer<TObj> BeginSelectBuilder(BaseConnector connector = null)
        {
            return new SelectQueryInitializer<TObj>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
        }
        #endregion

        #region Update
        /// <summary>
        /// Creates an update builder. Add a WHERE filter before execution.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable update query.</returns>
        public static UpdateQuery<TObj> BeginUpdate(BaseConnector connector = null)
        {
            return new UpdateQuery<TObj>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
        }
        #endregion

        #region Delete
        /// <summary>
        /// Creates a delete query. Add a WHERE filter before execution.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable delete query.</returns>
        public static DeleteQuery<TObj> BeginDelete(BaseConnector connector = null)
        {
            return new DeleteQuery<TObj>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
        }
        #endregion

        #region Truncate
        /// <summary>
        /// Creates a command to remove every row from the mapped table. SQLite TRUNCATE is unsupported.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A typed truncate query.</returns>
        public static TruncateQuery<TObj> BeginTruncate(BaseConnector connector = null)
        {
            return new TruncateQuery<TObj>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
        }
        #endregion

        #region StoredProcedure
        /// <summary>
        /// Creates a legacy stored-procedure query that loads typed JSON or a tabular result.
        /// </summary>
        /// <param name="name">The name of the existing stored procedure.</param>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A procedure query with no input parameters yet.</returns>
        public static StoredProcedureQuery<TObj> BeginStoredProcedure(string name, BaseConnector connector = null)
        {
            return new StoredProcedureQuery<TObj>(name, (connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
        }
        /// <summary>
        /// Creates a legacy stored-procedure query that loads typed JSON or a tabular result.
        /// </summary>
        /// <param name="name">The name of the existing stored procedure.</param>
        /// <param name="schema">The explicit procedure schema, or null for an unqualified procedure name.</param>
        /// <returns>A procedure query with no input parameters yet.</returns>
        public static StoredProcedureQuery<TObj> BeginStoredProcedure(string name, string schema)
        {
            return new StoredProcedureQuery<TObj>(name, schema);
        }
        #endregion

        /// <summary>
        /// Returns the cached table mapping for this model and the selected default schema.
        /// </summary>
        /// <param name="connector">The connector supplying the default schema, or null to use the current default connector's schema.</param>
        /// <returns>The mapped table metadata, including columns, primary key, and relationships.</returns>
        public static Table GetTableModel(BaseConnector connector = null)
        {
            return Table.GetCached<TObj>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema);
        }
    }
}
