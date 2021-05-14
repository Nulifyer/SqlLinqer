using SqlLinqer.Queries;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace SqlLinqer
{
    /// <summary>
    /// Includes the extension methods of the SqlLinqer library
    /// Should be inherited by classes with no primary key. For those with a primary key use <see cref="SqlLinqerObjectWithPrimaryKey{TObj, TKey}"/>
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public abstract class SqlLinqerObject<TObj> where TObj : SqlLinqerObject<TObj>, new()
    {
        /// <summary>
        /// Begins an insert query for the current object
        /// </summary>
        public SQLInsertQuery<TObj> Insert()
        {
            return Insert(this as TObj);
        }

        /// <summary>
        /// Begins a select query for <typeparamref name="TObj"/>
        /// Selects all columns at a recursion level of 1
        /// </summary>
        public static SQLSelectWhereQuery<TObj> Select()
        {
            return new SQLSelectWhereQuery<TObj>();
        }
        /// <summary>
        /// Begins a select query for <typeparamref name="TObj"/>
        /// Selects all columns at a recursion level of <paramref name="recursionLevel"/>
        /// </summary>
        public static SQLSelectWhereQuery<TObj> Select(int recursionLevel)
        {
            return new SQLSelectWhereQuery<TObj>(recursionLevel: recursionLevel);
        }
        /// <summary>
        /// Begins a select query for <typeparamref name="TObj"/>
        /// Selects only the listed columns
        /// </summary>
        /// <param name="expressions">Expressions that points to the properties/fields of the class to be selected</param>
        public static SQLSelectWhereQuery<TObj> Select(params Expression<Func<TObj, object>>[] expressions)
        {
            return new SQLSelectWhereQuery<TObj>(expressions);
        }

        /// <summary>
        /// Begins an insert query for <typeparamref name="TObj"/>
        /// </summary>
        /// <param name="obj">The object to Insert</param>
        public static SQLInsertQuery<TObj> Insert(TObj obj)
        {
            return new SQLInsertQuery<TObj>(obj);
        }
        /// <summary>
        /// Begins an insert query for <typeparamref name="TObj"/>
        /// </summary>
        /// <param name="objs"></param>
        public static SQLInsertManyQuery<TObj> Insert(IEnumerable<TObj> objs)
        {
            return new SQLInsertManyQuery<TObj>(objs);
        }

        /// <summary>
        /// Begins a update query for <typeparamref name="TObj"/>
        /// </summary>
        /// <param name="expression">A expression that points to the property/field of the class to be updated</param>
        /// <param name="value">The value to update to</param>
        public static SQLUpdateWhereQuery<TObj> Update(Expression<Func<TObj, object>> expression, object value)
        {
            return new SQLUpdateWhereQuery<TObj>().Update(expression, value);
        }

        /// <summary>
        /// Begins a delete statement with a where clause for <typeparamref name="TObj"/>
        /// If no where clauses are define the table will be truncated instead.
        /// </summary>
        public static SQLDeleteWhereQuery<TObj> DeleteWhere()
        {
            return new SQLDeleteWhereQuery<TObj>();
        }
    }
    /// <summary>
    /// Includes the extension methods of the SqlLinqer library
    /// Should be inherited by classes with a primary key. For those without a primary key use <see cref="SqlLinqerObject{TObj}"/>
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    /// <typeparam name="TKey">The type of value for the primary key</typeparam>
    public abstract class SqlLinqerObjectWithPrimaryKey<TObj, TKey> : SqlLinqerObject<TObj> where TObj : SqlLinqerObject<TObj>, new()
    {
        /// <summary>
        /// Begins an insert query for the current object
        /// </summary>
        public new SQLInsertPrimaryReturnQuery<TObj, TKey> Insert()
        {
            return Insert(this as TObj);
        }

        /// <summary>
        /// Begins an update query for the current object
        /// </summary>
        /// <param name="ignoreDefaults">If to update fields where the C# value if the default value</param>
        public SQLUpdateQuery<TObj, TKey> Update(bool ignoreDefaults = false)
        {
            return Update(this as TObj, ignoreDefaults);
        }

        /// <summary>
        /// Begins a delete query for the current object
        /// </summary>
        public SQLDeleteQuery<TObj, TKey> Delete()
        {
            return new SQLDeleteQuery<TObj, TKey>(this as TObj);
        }

        /// <summary>
        /// Begins an insert query for <typeparamref name="TObj"/>
        /// </summary>
        /// <param name="obj">The object to insert</param>
        public new static SQLInsertPrimaryReturnQuery<TObj, TKey> Insert(TObj obj)
        {
            return new SQLInsertPrimaryReturnQuery<TObj, TKey>(obj);
        }

        /// <summary>
        /// Begin a select query for <typeparamref name="TObj"/>
        /// Returns the <typeparamref name="TObj"/> where the primary key is <paramref name="primarykeyValue"/>
        /// </summary>
        /// <param name="primarykeyValue">The primary key value</param>
        public static SQLSelectQuery<TObj, TKey> Select(TKey primarykeyValue)
        {
            return new SQLSelectQuery<TObj, TKey>(primarykeyValue);
        }
        /// <summary>
        /// Begin stored procedure query that returns <typeparamref name="TObj"/>
        /// </summary>
        /// <param name="storedProcedureName">The name of the stored procedure</param>
        /// <param name="parameters">Optionally may include parameters to send to the storedProcedure, for <see cref="DBType.SQLServer"/> '@' is prepended to parameter names.</param>
        public static SQLStoredProcedureQuery<TObj> ExecStoredProcedure(string storedProcedureName, Dictionary<string, object> parameters = null)
        {
            return new SQLStoredProcedureQuery<TObj>(storedProcedureName, parameters);
        }

        /// <summary>
        /// Begins an update query for <typeparamref name="TObj"/>
        /// All columns of the object will be updated except the primary key.
        /// </summary>
        /// <param name="obj">The object to update</param>
        /// <param name="ignoreDefaults">If to update fields where the C# value if the default value</param>
        public static SQLUpdateQuery<TObj, TKey> Update(TObj obj, bool ignoreDefaults = false)
        {
            return new SQLUpdateQuery<TObj, TKey>(obj, ignoreDefaults);
        }

        /// <summary>
        /// Begins a delete query for <typeparamref name="TObj"/>
        /// This will delete the object from the table where the primary key matches
        /// </summary>
        /// <param name="primarykeyValue">The primary key value of the object to delete</param>
        public static SQLDeleteQuery<TObj, TKey> Delete(TKey primarykeyValue)
        {
            return new SQLDeleteQuery<TObj, TKey>(primarykeyValue);
        }
    }
}
