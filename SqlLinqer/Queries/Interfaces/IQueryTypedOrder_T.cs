using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed order query
    /// </summary>
    public interface IQueryTypedOrder<TQuery, TObj>
    {
        /// <summary>
        /// Add a order by statement to the query
        /// </summary>
        /// <param name="expression">The column expression to order by</param>
        /// <param name="dir">The direction of the order</param>
        TQuery OrderBy<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlDir dir);
        /// <summary>
        /// Add an additional order by statement to the query
        /// </summary>
        /// <param name="expression">The column expression to order by</param>
        /// <param name="dir">The direction of the order</param>
        TQuery ThenBy<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlDir dir);
    }
}