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
        /// <summary>
        /// Takes a shared lock on the selected root rows until the caller's transaction ends. Other transactions can read and share-lock the rows,
        /// but cannot change or delete them. Use it to keep a parent row in place while writing rows that refer to it.
        /// PostgreSQL and MySQL render FOR SHARE; SQL Server and Azure SQL render REPEATABLEREAD table hints. SQLite and DuckDB are not supported.
        /// On SQL Server and Azure SQL, another transaction can still take a lock for update on a shared row; its write then waits for the shared lock.
        /// A later call to ForUpdate or ForShare replaces an earlier one.
        /// </summary>
        /// <param name="wait">Whether to wait for, skip, or fail on rows another transaction has locked for update.</param>
        /// <returns>This query.</returns>
        TQuery ForShare(SqlRowLock wait = SqlRowLock.WAIT);
    }
}
