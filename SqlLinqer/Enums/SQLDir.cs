using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Sql order by directions
    /// </summary>
    public enum SqlDir
    {
        /// <summary>
        /// Ascending
        /// </summary>
        [Description("ASC")]
        ASC,
        /// <summary>
        /// Descending 
        /// </summary>
        [Description("DESC")]
        DESC
    }
}
