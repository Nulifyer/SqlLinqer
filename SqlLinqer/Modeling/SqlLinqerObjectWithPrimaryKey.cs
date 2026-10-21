using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Provides typed query factories, primary-key filters, and affected-key capture for a model with one mapped primary key.
    /// Mark the key member with SqlPrimaryKey; set dbGenerated to false for application-generated keys.
    /// </summary>
    /// <typeparam name="TObj">The concrete mapped model type.</typeparam>
    /// <typeparam name="TPrimaryKey">The primary key value type.</typeparam>
    public abstract class SqlLinqerPrimaryKeyObject<TObj, TPrimaryKey>
        : SqlLinqerObject<TObj>
        where TObj : class
    {
        #region Insert
        /// <summary>
        /// Creates an empty insert builder that captures inserted primary keys.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable insert query. Add rows with InsertAuto before executing.</returns>
        public static new InsertWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginInsert(BaseConnector connector = null)
        {
            var query = new InsertWithPrimaryKeyQuery<TObj, TPrimaryKey>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
            return query;
        }
        /// <summary>
        /// Creates an insert for this instance using automatic-insert attributes and captures its inserted primary key.
        /// Database-generated primary keys are omitted.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>An insert query containing this instance and its automatic-insert columns.</returns>
        public new InsertWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginInsertAuto(BaseConnector connector = null)
        {
            var query = BeginInsert(connector);
            query.InsertAuto(this as TObj);
            return query;
        }
        #endregion

        #region Select
        /// <summary>
        /// Creates a SELECT builder without selecting its output columns and filters it by the supplied primary key.
        /// </summary>
        /// <param name="primaryKeyValue">The key value used in the query's primary-key equality filter.</param>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable select query with a primary-key equality filter.</returns>
        public static SelectQuery<TObj> BeginSelect(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = BeginSelect(connector);
            query.RootWhere.AddComponent(new WhereColumnValue(query.Table.PrimaryKey, primaryKeyValue, SqlOp.EQ, SqlSubQueryOp.ANY));
            return query;
        }
        /// <summary>
        /// Creates a SELECT with automatic column and relationship selection and filters it by the supplied primary key.
        /// Relationships require SqlAutoSelect inclusion.
        /// </summary>
        /// <param name="primaryKeyValue">The key value used in the query's primary-key equality filter.</param>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable select query with a primary-key equality filter.</returns>
        public static SelectQuery<TObj> BeginSelectAuto(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = BeginSelectAuto(connector);
            query.RootWhere.AddComponent(new WhereColumnValue(query.Table.PrimaryKey, primaryKeyValue, SqlOp.EQ, SqlSubQueryOp.ANY));
            return query;
        }
        #endregion

        #region Update
        /// <summary>
        /// Creates an update builder with a primary-key filter.
        /// </summary>
        /// <param name="primaryKeyValue">The key value used in the query's primary-key equality filter.</param>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable update query filtered by the instance or supplied primary key.</returns>
        public static UpdateWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginUpdate(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = new UpdateWithPrimaryKeyQuery<TObj, TPrimaryKey>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
            query.RootWhere.AddComponent(new WhereColumnValue(query.Table.PrimaryKey, primaryKeyValue, SqlOp.EQ, SqlSubQueryOp.ANY));
            return query;
        }
        /// <summary>
        /// Creates assignments from this instance using automatic-update attributes with a primary-key filter.
        /// Includes null values and excludes the primary key.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable update query filtered by the instance or supplied primary key.</returns>
        public UpdateWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginUpdateAuto(BaseConnector connector = null)
        {
            var query = BeginUpdate(GetPrimaryKeyValue(connector), connector);
            query.UpdateAuto(this as TObj);
            return query;
        }
        /// <summary>
        /// Creates an update filtered by this instance's primary key, skipping null assignments unless marked SqlUpdateDefaultAlways.
        /// Includes zero and false, ignores automatic-update include/exclude attributes, and excludes the primary key.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable update query filtered by the instance or supplied primary key.</returns>
        public UpdateWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginUpdateNonDefaults(BaseConnector connector = null)
        {
            var query = BeginUpdate(GetPrimaryKeyValue(connector), connector);
            query.UpdateNonDefaults(this as TObj);
            return query;
        }
        #endregion

        #region Delete
        /// <summary>
        /// Creates a delete query. Add a WHERE filter before execution.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable delete query that captures deleted primary keys.</returns>
        public static new DeleteWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginDelete(BaseConnector connector = null)
        {
            var query = new DeleteWithPrimaryKeyQuery<TObj, TPrimaryKey>((connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
            return query;
        }
        /// <summary>
        /// Creates a delete query filtered by the supplied primary key.
        /// </summary>
        /// <param name="primaryKeyValue">The key value used in the query's primary-key equality filter.</param>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable delete query with a primary-key equality filter.</returns>
        public static DeleteQuery<TObj> BeginDelete(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = BeginDelete(connector);
            query.RootWhere.AddComponent(new WhereColumnValue(query.Table.PrimaryKey, primaryKeyValue, SqlOp.EQ, SqlSubQueryOp.ANY));
            return query;
        }
        /// <summary>
        /// Creates a delete query filtered by this instance's primary key.
        /// </summary>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A mutable delete query with a primary-key equality filter.</returns>
        public DeleteQuery<TObj> BeginDeleteAuto(BaseConnector connector = null)
        {
            var query = BeginDelete(GetPrimaryKeyValue(connector), connector);
            return query;
        }
        #endregion

        #region Exists
        /// <summary>
        /// Creates a query that checks whether the supplied primary key exists without loading the model.
        /// Check response State before interpreting the Boolean result; errors also return false.
        /// </summary>
        /// <param name="primaryKeyValue">The key value used in the query's primary-key equality filter.</param>
        /// <param name="connector">The connector retained by the new query, or null to use the current default at execution time. Its default schema supplies unmapped schemas during construction.</param>
        /// <returns>A query returning true for a matching key or false when no row matches.</returns>
        public static SelectPrimaryKeyExistsQuery<TObj, TPrimaryKey> BeginPrimaryKeyExists(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = new SelectPrimaryKeyExistsQuery<TObj, TPrimaryKey>(primaryKeyValue, (connector ?? SqlLinqer.Default.Connector)?.Options.DefaultSchema) { BoundConnector = connector };
            return query;
        }
        #endregion

        /// <summary>
        /// Assigns this instance's mapped primary-key member without changing the database.
        /// </summary>
        /// <param name="value">The primary key value to assign.</param>
        /// <param name="connector">An optional connector supplied with the model operation.</param>
        public void SetPrimaryKeyValue(TPrimaryKey value, BaseConnector connector = null)
        {
            var pk = GetPrimaryKey(connector ?? SqlLinqer.Default.Connector);
            pk.SetValue(this as TObj, value);
        }
        /// <summary>
        /// Reads this instance's mapped primary-key member without querying the database.
        /// </summary>
        /// <param name="connector">An optional connector supplied with the model operation.</param>
        /// <returns>The value of the mapped primary-key member.</returns>
        public TPrimaryKey GetPrimaryKeyValue(BaseConnector connector = null)
        {
            return GetPrimaryKeyValue(false, connector ?? SqlLinqer.Default.Connector);
        }

        private ReflectedColumn GetPrimaryKey(BaseConnector connector = null)
        {
            var table = GetTableModel();
            var primary_key = table.PrimaryKey as ReflectedColumn;
            if (primary_key == null)
                throw new System.FormatException($"No defined primary key");
            return primary_key;
        }
        private TPrimaryKey GetPrimaryKeyValue(bool throwIfNull = true, BaseConnector connector = null)
        {
            var pk = GetPrimaryKey(connector ?? SqlLinqer.Default.Connector);
            var pkValue = pk.GetValue(this as TObj);
            if (throwIfNull && pkValue == null)
                throw new System.NullReferenceException($"Primary key value is null");
            return (TPrimaryKey)pkValue;
        }
    }
}
