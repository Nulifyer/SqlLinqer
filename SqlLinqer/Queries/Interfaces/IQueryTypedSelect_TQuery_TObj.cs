using System;
using System.Linq.Expressions;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed select query
    /// </summary>
    public interface IQueryTypedSelect<TQuery, TObj>
    {
        /// <summary>
        /// If the expression targets a column, the column will be added to the query.
        /// If the expression targets a relationship, all root columns will be added to the relationship.
        /// </summary>
        /// <param name="expression">An expression that targets a column or relationship</param>
        /// <returns>The current query instance</returns>
        TQuery Select<TProperty>(Expression<Func<TObj, TProperty>> expression);
    }
}