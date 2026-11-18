using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// How a locking SELECT treats rows that another transaction has already locked.
    /// </summary>
    public enum SqlLockWait
    {
        /// <summary>
        /// Wait for the other transaction to finish.
        /// </summary>
        [Description("")]
        WAIT,
        /// <summary>
        /// Leave locked rows out of the result. Use it to claim work from a queue.
        /// </summary>
        [Description("SKIP LOCKED")]
        SKIPLOCKED,
        /// <summary>
        /// Fail immediately when a selected row is locked.
        /// </summary>
        [Description("NOWAIT")]
        NOWAIT,
    }
}
