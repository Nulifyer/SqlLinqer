using System;
using System.Linq.Expressions;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed write query that can return selected modified columns.
    /// </summary>
    public interface IQueryTypedOutput<TQuery, TObj>
    {
        /// <summary>
        /// Adds a modified column to the write output projection. ExecuteWithOutput returns those columns as a DataTable.
        /// MySQL modified-row output is unsupported. Dispose the returned DataTable after use.
        /// </summary>
        /// <typeparam name="TProperty">The output member type.</typeparam>
        /// <param name="expression">The mapped column to return after an insert or update, or before a delete.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery Output<TProperty>(Expression<Func<TObj, TProperty>> expression);
    }
}
