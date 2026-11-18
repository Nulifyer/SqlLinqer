namespace SqlLinqer.Components.Select
{
    /// <summary>The row lock a SELECT takes on the root table's selected rows.</summary>
    public sealed class RowLock
    {
        /// <summary>True for a shared lock, which other transactions can also take. False locks the rows for update.</summary>
        public readonly bool Shared;
        /// <summary>How the SELECT treats rows that another transaction has already locked.</summary>
        public readonly SqlLockWait Wait;

        /// <summary>Creates a row lock.</summary>
        public RowLock(bool shared, SqlLockWait wait)
        {
            Shared = shared;
            Wait = wait;
        }
    }
}
