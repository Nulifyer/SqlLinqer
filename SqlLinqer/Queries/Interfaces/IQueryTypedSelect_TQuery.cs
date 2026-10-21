using System.Collections.Generic;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Adds mapped columns and nested relationships to a SELECT projection.
    /// </summary>
    /// <typeparam name="TQuery">The fluent query type.</typeparam>
    public interface IQueryTypedSelect<TQuery>
    {
        /// <summary>
        /// Adds columns and relationships allowed by SqlAutoSelect and SqlAutoSelectExclude. Relationships require automatic-selection inclusion.
        /// Scalar columns participate by default; explicit Select can include automatically excluded members.
        /// </summary>
        /// <param name="recursive">True to apply automatic selection to deeper relationships. False still selects included immediate relationships and their automatic columns.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery SelectAuto(bool recursive = true);
        /// <summary>
        /// Adds every mapped scalar column of the root model without loading relationship models.
        /// Includes automatic-selection exclusions; SqlIgnore members remain unmapped.
        /// </summary>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery SelectRootColumns();
        /// <summary>
        /// Adds a mapped column to the projection. Related-column paths create nested relationship projections as needed.
        /// </summary>
        /// <param name="column">The mapped column on the root table or a related table.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery Select(Column column);
        /// <summary>
        /// Loads the related model or collection with its mapped root columns. Missing objects become null and missing collections become empty lists.
        /// </summary>
        /// <param name="relationship">The mapped relationship to load.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery Select(Relationship relationship);
        /// <summary>
        /// Selects the mapped column or relationship identified by a C# member path.
        /// </summary>
        /// <param name="path">Member names from the root model through any related models. These are CLR member names, not database column names.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        /// <exception cref="SqlLinqer.Exceptions.SqlPathNotFoundException">The path does not identify a mapped column or relationship.</exception>
        TQuery Select(IEnumerable<string> path);
    }
}
