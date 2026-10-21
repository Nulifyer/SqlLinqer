using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Configures a mapped member's default sort direction and optional ordering priority for OrderByAuto.
    /// Explicit OrderBy replaces ordering; ThenBy appends a tie-breaker.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoOrderBy : Attribute
    {
        /// <summary>
        /// The automatic sorting direction. The constructor default is ascending.
        /// </summary>
        public SqlDir Dir { get; }

        /// <summary>
        /// The explicit priority among automatic ordering members, or null when no priority was supplied.
        /// </summary>
        public int? Order { get; }

        /// <summary>
        /// Configures automatic ordering for this member with the supplied direction.
        /// </summary>
        /// <param name="dir">The automatic sort direction. Defaults to ascending.</param>
        public SqlAutoOrderBy(SqlDir dir = SqlDir.ASC)
        {
            Dir = dir;
        }

        /// <summary>
        /// Configures the sort direction and relative priority of this automatic ordering member.
        /// </summary>
        /// <param name="dir">The automatic sort direction.</param>
        /// <param name="order">The priority used to order automatic sorting expressions.</param>
        public SqlAutoOrderBy(SqlDir dir, int order) : this(dir)
        {
            Order = order;
        }
    }
}
