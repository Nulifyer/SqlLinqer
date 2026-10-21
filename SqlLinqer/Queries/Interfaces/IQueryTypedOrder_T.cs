using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Defines ordering for selected rows, including columns reached through mapped relationships.
    /// </summary>
    /// <typeparam name="TQuery">The fluent query type.</typeparam>
    /// <typeparam name="TObj">The root or projected result model.</typeparam>
    public interface IQueryTypedOrder<TQuery, TObj>
    {
        /// <summary>
        /// Replaces the current ordering with the selected column and direction.
        /// Use a unique tie-breaker when paging rows with equal sort values.
        /// </summary>
        /// <typeparam name="TProperty">The sorting member type.</typeparam>
        /// <param name="expression">The root, related, or aggregate projection member used for sorting.</param>
        /// <param name="dir">Ascending or descending sort direction.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OrderBy<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlDir dir);
        /// <summary>
        /// Appends a secondary ordering column without removing earlier ordering.
        /// Use a unique tie-breaker when paging rows with equal sort values.
        /// </summary>
        /// <typeparam name="TProperty">The sorting member type.</typeparam>
        /// <param name="expression">The root, related, or aggregate projection member used for sorting.</param>
        /// <param name="dir">Ascending or descending sort direction.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery ThenBy<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlDir dir);
    }
}
