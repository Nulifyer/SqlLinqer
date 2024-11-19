using System;
using System.Linq.Expressions;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed insert query
    /// </summary>
    public interface IQueryTypedOutput<TQuery, TObj>
    {
        /// <summary>
        /// Add a column to the output
        /// </summary>
        /// <param name="expression">The column to output</param>
        TQuery Output<TProperty>(Expression<Func<TObj, TProperty>> expression);
    }
}