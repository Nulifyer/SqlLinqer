using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Sql sub query operators
    /// </summary>
    public enum SqlSubQueryOp
    {
        /// <summary>
        /// Matches any value in Enumerable. Use only for one to many relationships.
        /// </summary>
        [Description("ANY")]
        ANY,
        /// <summary>
        /// Matches all values in Enumerable. Use only for one to many relationships.
        /// </summary>
        [Description("ALL")]
        ALL,
    }
}
