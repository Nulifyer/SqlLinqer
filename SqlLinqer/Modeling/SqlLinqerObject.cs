using SqlLinqer.Components.Modeling;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Includes the extension methods of the SqlLinqer library
    /// Should be inherited by classes with no primary key. For those with a primary key use <see cref="SqlLinqerPrimaryKeyObject{TObj, TKey}"/>
    /// </summary>
    /// <typeparam name="TObj">The data model that represents the database table and its relationships to other data models</typeparam>
    public abstract class SqlLinqerObject<TObj> where TObj : class
    {
        #region Insert
        /// <summary>
        /// Create a insert query for the object
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A insert query for the defined object type.</returns>
        public static InsertQuery<TObj> BeginInsert(BaseConnector connector = null)
        {
            return new InsertQuery<TObj>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
        }
        /// <summary>
        /// Create an auto insert query
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>
        /// A insert query for the defined object type. The inserted columns are based off the
        /// <see cref="SqlLinqer.Modeling.SqlAutoInsert"/> and <see cref="SqlLinqer.Modeling.SqlAutoInsertExclude"/> attributes.
        /// </returns>
        public InsertQuery<TObj> BeginInsertAuto(BaseConnector connector = null)
        {
            var query = BeginInsert(connector ?? SqlLinqer.Default.Connector);
            query.InsertAuto(this as TObj);
            return query;
        }
        #endregion

        #region Select
        /// <summary>
        /// Create a select query for the object
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A select query for the defined object type.</returns>
        public static SelectQuery<TObj> BeginSelect(BaseConnector connector = null)
        {
            return new SelectQuery<TObj>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
        }
        /// <summary>
        /// Create a select query for the object
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>
        /// A select query.
        /// The selected columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoSelect"/>
        /// and <see cref="SqlLinqer.Modeling.SqlAutoSelectExclude"/> attributes.
        /// </returns>
        public static SelectQuery<TObj> BeginSelectAuto(BaseConnector connector = null)
        {
            var query = BeginSelect(connector ?? SqlLinqer.Default.Connector);
            query.SelectAuto();
            return query;
        }
        #endregion

        #region Select Aggregate
        /// <summary>
        /// Create a select aggregate query for the object
        /// </summary>
        /// <returns>A select aggregate query for the defined object type.</returns>
        public static SelectQueryInitializer<TObj> BeginSelectBuilder(BaseConnector connector = null)
        {
            return new SelectQueryInitializer<TObj>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
        }
        #endregion

        #region Update
        /// <summary>
        /// Create an update query for the object
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>An update query for the defined object type.</returns>
        public static UpdateQuery<TObj> BeginUpdate(BaseConnector connector = null)
        {
            return new UpdateQuery<TObj>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
        }
        #endregion

        #region Delete
        /// <summary>
        /// Create a delete query for the object
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A delete query for the defined object type.</returns>
        public static DeleteQuery<TObj> BeginDelete(BaseConnector connector = null)
        {
            return new DeleteQuery<TObj>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
        }
        #endregion

        #region Truncate
        /// <summary>
        /// Create a truncate query for the object
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A truncate query for the defined object type.</returns>
        public static TruncateQuery<TObj> BeginTruncate(BaseConnector connector = null)
        {
            return new TruncateQuery<TObj>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
        }
        #endregion

        #region StoredProcedure
        /// <summary>
        /// Create a stored procedure query for the object
        /// </summary>
        /// <param name="name">The name of the stored procedure</param>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A stored procedure query for the defined object type.</returns>
        public static StoredProcedureQuery<TObj> BeginStoredProcedure(string name, BaseConnector connector = null)
        {
            return BeginStoredProcedure(name, connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
        }
        /// <summary>
        /// Create a stored procedure query for the object
        /// </summary>
        /// <param name="name">The name of the stored procedure</param>
        /// <param name="schema">The schema of the stored procedure</param>
        /// <returns>A stored procedure query for the defined object type.</returns>
        public static StoredProcedureQuery<TObj> BeginStoredProcedure(string name, string schema)
        {
            return new StoredProcedureQuery<TObj>(name, schema);
        }
        #endregion

        /// <summary>
        /// Gets the table model of the type
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>The primary key value of the object.</returns>
        public static Table GetTableModel(BaseConnector connector = null)
        {
            return Table.GetCached<TObj>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
        }
    }
}
