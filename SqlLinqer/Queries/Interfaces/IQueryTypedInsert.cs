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
        /// Inserts the database server's current time into the named columns of every row, instead of the rows' own values.
        /// The columns are inserted whether or not automatic insertion includes them. Add the rows with InsertAuto.
        /// The time follows ServerTimeOperation: UTC on SQL Server, Azure SQL, MySQL, and SQLite, and the current timestamp with its zone on PostgreSQL and DuckDB.
        /// </summary>
        /// <param name="members">One or several root members that receive the server time.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery InsertServerTime(params System.Linq.Expressions.Expression<System.Func<TObj, object>>[] members);
    }
}
