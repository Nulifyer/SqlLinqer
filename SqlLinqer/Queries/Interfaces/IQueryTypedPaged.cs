namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Controls row limits, paging, and DISTINCT on a mutable SELECT query.
    /// </summary>
    /// <typeparam name="TQuery">The fluent query type returned by each operation.</typeparam>
    public interface IQueryTypedPaged<TQuery>
    {
        /// <summary>
        /// Limits the selected rows and clears any page limit or offset. Total-count execution still counts the unpaged projection.
        /// </summary>
        /// <param name="num">The maximum row count. Zero clears the TOP limit.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery Top(int num);
        /// <summary>
        /// Selects a one-based page and replaces previous TOP, limit, and offset settings.
        /// Use OrderBy and ThenBy for stable page ordering.
        /// </summary>
        /// <param name="page">The page number, starting at 1. Values below 1 use page 1.</param>
        /// <param name="pageSize">The number of rows per page. Negative values use zero.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        /// <example>
        /// <code>
        /// var page = User.BeginSelect(connector).SelectRootColumns()
        ///     .OrderBy(x =&gt; x.Id, SqlDir.ASC).Page(1, 20);
        /// </code>
        /// </example>
        TQuery Page(int page, int pageSize);
        /// <summary>
        /// Sets a row limit and a zero-based offset, replacing previous TOP and page settings.
        /// Total-count execution removes root paging and preserves nested query paging.
        /// </summary>
        /// <param name="limit">The maximum number of rows to return.</param>
        /// <param name="offset">The number of rows to skip before returning rows.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery Limit(int limit, int offset);
        /// <summary>
        /// Enables or disables DISTINCT for the selected projection. Total-count execution counts distinct projected rows.
        /// </summary>
        /// <param name="distinct">True to remove duplicate projected rows. Defaults to true.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery Distinct(bool distinct = true);
    }
}
