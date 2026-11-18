using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Stands for the database server's clock where a query otherwise takes an application value.
    /// </summary>
    public enum SqlServerTime
    {
        /// <summary>
        /// The server's current time, as ServerTimeOperation assigns it.
        /// </summary>
        [Description("NOW")]
        NOW,
    }
}
