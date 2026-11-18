namespace SqlLinqer
{
    /// <summary>
    /// How a locking SELECT treats rows that another transaction has already locked.
    /// </summary>
    public enum SqlRowLock
    {
        /// <summary>
        /// Wait for the other transaction to finish.
        /// </summary>
        WAIT,
        /// <summary>
        /// Leave locked rows out of the result. Use it to claim work from a queue.
        /// </summary>
        SKIPLOCKED,
        /// <summary>
        /// Fail immediately when a selected row is locked.
        /// </summary>
        NOWAIT,
    }
}
