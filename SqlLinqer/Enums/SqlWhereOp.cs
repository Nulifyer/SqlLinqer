using System.ComponentModel;

namespace SqlLinqer
{
    /// <summary>
    /// Sql Where group operators
    /// </summary>
    public enum SqlWhereOp
    {
        /// <summary>
        /// And
        /// </summary>
        [Description("AND")]
        AND,
        /// <summary>
        /// Or
        /// </summary>
        [Description("OR")]
        OR
    }
}
