using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Stands for the database server's clock where a query otherwise takes an application value:
    /// in a filter, an update, an inserted column, or a conflict update. No application time is sent.
    /// </summary>
    public enum SqlTime
    {
        /// <summary>
        /// The server's current time. SQL Server, Azure SQL, MySQL, and SQLite use the current UTC time.
        /// PostgreSQL and DuckDB use the current timestamp with its time zone, which is an exact instant in a
        /// time-zone-aware column and the session time zone's local time in a plain timestamp column.
        /// </summary>
        [Description("NOW")]
        NOW,
    }
}
