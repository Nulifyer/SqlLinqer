using System.Collections.Generic;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed select query
    /// </summary>
    public interface IQueryTypedSelect<TQuery>
    {
        /// <summary>
        /// Add select statements to the query.
        /// The selected columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoSelect"/> 
        /// and <see cref="SqlLinqer.Modeling.SqlAutoSelectExclude"/> attributes.
        /// </summary>
        /// <param name="recursive">If true, the select auto will be applied to all sub queries</param>
        /// <returns>The current query instance</returns>
        TQuery SelectAuto(bool recursive = true);
        /// <summary>
        /// Add select statements to the query.
        /// The selected columns are only the columns defined on the root model of the query.
        /// </summary>
        /// <returns>The current query instance</returns>
        TQuery SelectRootColumns();
        /// <summary>
        /// Add a column to the select. The column's table and related parent tables are joined to the root query.
        /// </summary>
        /// <param name="column">The column to add to the select.</param>
        /// <returns>The current query instance</returns>
        TQuery Select(Column column);
        /// <summary>
        /// Add a column to the select. The column's table and related parent tables are joined to the root query.
        /// </summary>
        /// <param name="relationship">The relationship to add to the select.</param>
        /// <returns>The current query instance</returns>
        TQuery Select(Relationship relationship);
        /// <summary>
        /// Add a column to the select. The column's table and related parent tables are joined to the root query.
        /// </summary>
        /// <param name="path">The member path of a column or relationship to add to the select.</param>
        /// <returns>The current query instance</returns>
        TQuery Select(IEnumerable<string> path);
    }
}