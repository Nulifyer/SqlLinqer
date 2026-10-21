using System.Collections.Generic;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Defines ordering for selected rows, including columns reached through mapped relationships.
    /// </summary>
    /// <typeparam name="TQuery">The fluent query type.</typeparam>
    public interface IQueryTypedOrder<TQuery>
    {
        /// <summary>
        /// Adds ordering configured by SqlAutoOrderBy, optionally including related query projections.
        /// </summary>
        /// <param name="recursive">True to include automatic ordering on related subqueries. Defaults to true.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OrderByAuto(bool recursive = true);
        /// <summary>
        /// Replaces the current ordering with the selected column and direction.
        /// Use a unique tie-breaker when paging rows with equal sort values.
        /// </summary>
        /// <param name="column">The mapped column used for sorting.</param>
        /// <param name="dir">Ascending or descending sort direction.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OrderBy(Column column, SqlDir dir);
        /// <summary>
        /// Appends a secondary ordering column without removing earlier ordering.
        /// Use a unique tie-breaker when paging rows with equal sort values.
        /// </summary>
        /// <param name="column">The mapped column used for sorting.</param>
        /// <param name="dir">Ascending or descending sort direction.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery ThenBy(Column column, SqlDir dir);
    }
}
