using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Sql where clause array operators
    /// </summary>
    public enum SqlArrayOp
    {
        /// <summary>
        /// Target is any value in the list
        /// </summary>
        [Description("IN")]
        IN,
        /// <summary>
        /// Target is NOT any value in the list
        /// </summary>
        [Description("NOT IN")]
        NOTIN,
        /// <summary>
        /// Target is any value in the list
        /// </summary>
        [Description("IN")]
        ANY,
        /// <summary>
        /// Target is any value in the list
        /// </summary>
        [Description("IN")]
        ALL,
        /// <summary>
        /// Target is any value in the list
        /// </summary>
        [Description("NOT IN")]
        NOTANY,
        /// <summary>
        /// Target is any value in the list
        /// </summary>
        [Description("NOT IN")]
        NOTALL,
    }    
}
