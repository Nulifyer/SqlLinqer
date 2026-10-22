using System;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer
{
    /// <summary>Assigns diagnostic names without changing generated SQL.</summary>
    public static class QueryDiagnosticExtensions
    {
        /// <summary>Names a query for logging and tracing. Capture preserves the name at capture time.</summary>
        /// <typeparam name="TQuery">The concrete query builder.</typeparam>
        /// <param name="query">The query to name.</param>
        /// <param name="name">A nonempty application operation name, such as users.search.</param>
        /// <returns>The same concrete query builder.</returns>
        public static TQuery Tag<TQuery>(this TQuery query, string name) where TQuery : BaseQuery
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A query name is required.", nameof(name));
            query.QueryName = name;
            return query;
        }
    }
}
