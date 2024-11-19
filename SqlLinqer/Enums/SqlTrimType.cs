using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Sql trim type options
    /// </summary>
    public enum SqlTrimType
    {
        /// <summary>
        /// Leading characters only
        /// </summary>
        [Description("LEADING")]
        LEADING,
        /// <summary>
        /// Trailing characters only
        /// </summary>
        [Description("TRAILING")]
        TRAILING,
        /// <summary>
        /// Both leading and trailing characters
        /// </summary>
        [Description("BOTH")]
        BOTH,
    }
}