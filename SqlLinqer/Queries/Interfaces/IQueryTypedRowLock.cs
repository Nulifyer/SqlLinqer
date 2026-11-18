namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Locks the rows a query selects.
    /// </summary>
    /// <typeparam name="TQuery">The query type being returned</typeparam>
    public interface IQueryTypedRowLock<TQuery>
    {
        /// <summary>
        /// Locks the selected root rows for update until the caller's transaction ends. Pass that transaction when executing;
        /// without one the lock is released as soon as the read completes.
        /// PostgreSQL and MySQL render FOR UPDATE; SQL Server and Azure SQL render UPDLOCK table hints. SQLite and DuckDB are not supported.
        /// </summary>
        /// <param name="wait">Whether to wait for, skip, or fail on rows another transaction has locked.</param>
        /// <returns>This query.</returns>
        TQuery ForUpdate(SqlRowLock wait = SqlRowLock.WAIT);
    }
}
