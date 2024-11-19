using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Includes the extension methods of the SqlLinqer library
    /// Should be inherited by classes with a primary key. For those without a primary key use <see cref="SqlLinqerObject{TObj}"/>
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    /// <typeparam name="TPrimaryKey">The type of value for the primary key</typeparam>
    public abstract class SqlLinqerPrimaryKeyObject<TObj, TPrimaryKey>
        : SqlLinqerObject<TObj>
        where TObj : class
    {
        #region Insert
        /// <summary>
        /// Create an insert with primary key query
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A insert query for the defined object type. The query will ouput the inserted primary keys.</returns>
        public static new InsertWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginInsert(BaseConnector connector = null)
        {
            var query = new InsertWithPrimaryKeyQuery<TObj, TPrimaryKey>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
            return query;
        }
        /// <summary>
        /// Create an auto insert with primary key query
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A insert query for the defined object type. The query will output the inserted primary keys. The inserted columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoInsert"/> and <see cref="SqlLinqer.Modeling.SqlAutoInsertExclude"/> attributes.</returns>
        public new InsertWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginInsertAuto(BaseConnector connector = null)
        {
            var query = BeginInsert(connector ?? SqlLinqer.Default.Connector);
            query.InsertAuto(this as TObj);
            return query;
        }
        #endregion

        #region Select
        /// <summary>
        /// Create a select with primary key query
        /// </summary>
        /// <param name="primaryKeyValue">The target primary key value</param>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A select query where the primary key == {primaryKeyValue}.</returns>
        public static SelectQuery<TObj> BeginSelect(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = BeginSelect(connector ?? SqlLinqer.Default.Connector);
            query.RootWhere.AddComponent(new WhereColumnValue(query.Table.PrimaryKey, primaryKeyValue, SqlOp.EQ, SqlSubQueryOp.ANY));
            return query;
        }
        /// <summary>
        /// Create a select with primary key query
        /// </summary>
        /// <param name="primaryKeyValue">The target primary key value</param>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>
        /// A select query where the primary key == {primaryKeyValue}.
        /// The selected columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoSelect"/> 
        /// and <see cref="SqlLinqer.Modeling.SqlAutoSelectExclude"/> attributes.
        /// </returns>
        public static SelectQuery<TObj> BeginSelectAuto(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = BeginSelectAuto(connector ?? SqlLinqer.Default.Connector);
            query.RootWhere.AddComponent(new WhereColumnValue(query.Table.PrimaryKey, primaryKeyValue, SqlOp.EQ, SqlSubQueryOp.ANY));
            return query;
        }
        #endregion

        #region Update
        /// <summary>
        /// Create an update with primary key query
        /// </summary>
        /// <param name="primaryKeyValue">The target primary key value</param>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A update query where the primary key == {primaryKeyValue}.</returns>
        public static UpdateWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginUpdate(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = new UpdateWithPrimaryKeyQuery<TObj, TPrimaryKey>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
            query.RootWhere.AddComponent(new WhereColumnValue(query.Table.PrimaryKey, primaryKeyValue, SqlOp.EQ, SqlSubQueryOp.ANY));
            return query;
        }
        /// <summary>
        /// Create an auto update with primary key query
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>
        /// A update query where the primary key == {primaryKeyValue}.
        /// The value for the primary key comes from the column defined as the primary key.
        /// The updated columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoUpdate"/> and <see cref="SqlLinqer.Modeling.SqlAutoUpdateExclude"/> attributes.
        /// </returns>
        public UpdateWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginUpdateAuto(BaseConnector connector = null)
        {
            connector = connector ?? SqlLinqer.Default.Connector;
            var query = BeginUpdate(GetPrimaryKeyValue(connector), connector);
            query.UpdateAuto(this as TObj);
            return query;
        }
        /// <summary>
        /// Create a update non-default value with primary key query
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>
        /// A update query where the primary key == {primaryKeyValue}.
        /// The value for the primary key comes from the column defined as the primary key.
        /// The updated columns are based off the column value being equal to the C# term 'default'.
        /// </returns>
        public UpdateWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginUpdateNonDefaults(BaseConnector connector = null)
        {
            connector = connector ?? SqlLinqer.Default.Connector;
            var query = BeginUpdate(GetPrimaryKeyValue(connector), connector);
            query.UpdateNonDefaults(this as TObj);
            return query;
        }
        #endregion

        #region Delete
        /// <summary>
        /// Create an insert with primary key query
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A insert query for the defined object type. The query will ouput the inserted primary keys.</returns>
        public static new DeleteWithPrimaryKeyQuery<TObj, TPrimaryKey> BeginDelete(BaseConnector connector = null)
        {
            var query = new DeleteWithPrimaryKeyQuery<TObj, TPrimaryKey>(connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
            return query;
        }
        /// <summary>
        /// Create a delete with primary key query
        /// </summary>
        /// <param name="primaryKeyValue">The target primary key value</param>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A delete query where the primary key == {primaryKeyValue}.</returns>
        public static DeleteQuery<TObj> BeginDelete(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = BeginDelete(connector ?? SqlLinqer.Default.Connector);
            query.RootWhere.AddComponent(new WhereColumnValue(query.Table.PrimaryKey, primaryKeyValue, SqlOp.EQ, SqlSubQueryOp.ANY));
            return query;
        }
        /// <summary>
        /// Create an auto delete with primary key query
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>
        /// A delete query where the primary key == {primaryKeyValue}.
        /// The value for the primary key comes from the column defined as the primary key.
        /// </returns>
        public DeleteQuery<TObj> BeginDeleteAuto(BaseConnector connector = null)
        {
            connector = connector ?? SqlLinqer.Default.Connector;
            var query = BeginDelete(GetPrimaryKeyValue(connector), connector);
            return query;
        }
        #endregion

        #region Exists
        /// <summary>
        /// Create a exists select with primary key query
        /// </summary>
        /// <param name="primaryKeyValue">The target primary key value</param>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>A delete query where the primary key == {primaryKeyValue}.</returns>
        public static SelectPrimaryKeyExistsQuery<TObj, TPrimaryKey> BeginPrimaryKeyExists(TPrimaryKey primaryKeyValue, BaseConnector connector = null)
        {
            var query = new SelectPrimaryKeyExistsQuery<TObj, TPrimaryKey>(primaryKeyValue, connector?.Options.DefaultSchema ?? SqlLinqer.Default.Connector?.Options.DefaultSchema);
            return query;
        }
        #endregion

        /// <summary>
        /// Sets the primary key value of the object
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <param name="connector">The connector to pull information from</param>
        public void SetPrimaryKeyValue(TPrimaryKey value, BaseConnector connector = null)
        {
            var pk = GetPrimaryKey(connector ?? SqlLinqer.Default.Connector);
            pk.SetValue(this as TObj, value);
        }
        /// <summary>
        /// Gets the primary key value of the object
        /// </summary>
        /// <param name="connector">The connector to pull information from</param>
        /// <returns>The primary key value of the object.</returns>
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
