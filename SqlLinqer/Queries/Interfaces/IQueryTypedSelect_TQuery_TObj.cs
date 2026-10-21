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
        /// Selects a mapped column or loads a relationship with its root columns. Collection members can be traversed with First().
        /// Missing single relationships load as null; empty collection relationships load as empty collections.
        /// </summary>
        /// <typeparam name="TProperty">The selected member type.</typeparam>
        /// <param name="expression">A member-access expression rooted in the model. Use collection.First().Member to traverse a collection relationship.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        /// <example>
        /// <code>
        /// var query = User.BeginSelect(connector)
        ///     .Select(x =&gt; x.Id).Select(x =&gt; x.Permissions);
        /// </code>
        /// </example>
        TQuery Select<TProperty>(Expression<Func<TObj, TProperty>> expression);
    }
}