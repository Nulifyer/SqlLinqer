using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Used to define a column name that is different from the property/field name
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoOrderBy : Attribute
    {
        /// <summary>
        /// The direction of the order by
        /// </summary>
        public SqlDir Dir { get; }

        /// <summary>
        /// The type in the database
        /// </summary>
        public int? Order { get; }

        /// <summary>
        /// Used to define a column name that is different from the property/field name
        /// </summary>
        /// <param name="dir">The direction of the order by</param>
        public SqlAutoOrderBy(SqlDir dir = SqlDir.ASC)
        {
            Dir = dir;
        }

        /// <summary>
        /// Used to define a column name that is different from the property/field name
        /// </summary>
        /// <param name="dir">The direction of the order by</param>
        /// <param name="order">The type in the database</param>
        public SqlAutoOrderBy(SqlDir dir, int order) : this(dir)
        {
            Order = order;
        }
    }
}
