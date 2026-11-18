using System.Collections.Generic;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Adds model rows to an INSERT query using automatic-insert mappings.
    /// </summary>
    /// <typeparam name="TQuery">The fluent query type.</typeparam>
    /// <typeparam name="TObj">The model type whose mapped values are inserted.</typeparam>
    public interface IQueryTypedInsert<TQuery, TObj>
    {
        /// <summary>
        /// Adds one row using SqlAutoInsert and SqlAutoInsertExclude. Database-generated primary keys are omitted.
        /// </summary>
        /// <param name="obj">The model instance to insert.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery InsertAuto(TObj obj);
        /// <summary>
        /// Adds rows using automatic-insert mappings. Execution batches them within the configured and native database limits.
        /// </summary>
        /// <param name="objs">The model instances to append, in enumeration order.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery InsertAuto(IEnumerable<TObj> objs);
        /// <summary>
        /// Inserts a server value into one column of every row, instead of the rows' own values for that column.
        /// The column is inserted whether or not automatic insertion includes it. Add the rows with InsertAuto.
        /// </summary>
        /// <typeparam name="TProperty">The target member type.</typeparam>
        /// <param name="expression">The root member that receives the value.</param>
        /// <param name="value">Use SqlTime.NOW to insert the database server's current time.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery Insert<TProperty>(System.Linq.Expressions.Expression<System.Func<TObj, TProperty>> expression, SqlTime value);
    }
}
