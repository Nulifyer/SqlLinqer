namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed paged query
    /// </summary>
    public interface IQueryTypedPaged<TQuery>
    {
        /// <summary>
        /// select query TOP
        /// </summary>
        /// <param name="num">The TOP amount</param>
        TQuery Top(int num);
        /// <summary>
        /// applies paging logic to the select
        /// </summary>
        /// <param name="page">The page to get</param>
        /// <param name="pageSize">The size of the pages</param>
        TQuery Page(int page, int pageSize);
        /// <summary>
        /// applies limit offset logic to the select
        /// </summary>
        /// <param name="limit">The limit amount</param>
        /// <param name="offset">The offset amount</param>
        TQuery Limit(int limit, int offset);
        /// <summary>
        /// To use DISTINCT or not
        /// </summary>
        /// <param name="distinct">To apply DISTINCT or not</param>
        TQuery Distinct(bool distinct = true);
    }
}