using System.Collections.Generic;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed order query
    /// </summary>
    public interface IQueryTypedOrder<TQuery>
    {
        /// <summary>
        /// Recursively applies an auto order by to the query and it's sub queries
        /// </summary>
        /// <param name="recursive">If true, auto order will be applied to all subqueries</param>
        TQuery OrderByAuto(bool recursive = true);
        /// <summary>
        /// Add a order by statement to the query
        /// </summary>
        /// <param name="column">The column to order by</param>
        /// <param name="dir">The direction of the order</param>
        TQuery OrderBy(Column column, SqlDir dir);
        /// <summary>
        /// Add an additional order by statement to the query
        /// </summary>
        /// <param name="column">The column to order by</param>
        /// <param name="dir">The direction of the order</param>
        TQuery ThenBy(Column column, SqlDir dir);
    }
}