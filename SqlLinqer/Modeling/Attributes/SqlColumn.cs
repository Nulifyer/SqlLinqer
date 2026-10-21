using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a public CLR member to a database column and can override its parameter DbType.
    /// Fluent expressions and relationship attributes use CLR member names; rendered SQL uses the mapped database name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlColumn : Attribute
    {
        /// <summary>
        /// The database name for this property/field
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The explicit parameter binding type, or null when no override was supplied. This attribute does not alter the database column definition.
        /// </summary>
        public readonly System.Data.DbType? DbType;

        /// <summary>
        /// Maps a model member to a named database column.
        /// </summary>
        /// <param name="name">The existing database column name.</param>
        public SqlColumn(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Maps a model member to a named database column with an explicit parameter type.
        /// </summary>
        /// <param name="name">The existing database column name.</param>
        /// <param name="dbType">An explicit parameter binding type, such as DateTime2 for a high-precision SQL Server timestamp.</param>
        public SqlColumn(string name, System.Data.DbType dbType)
            : this(name)
        {
            DbType = dbType;
        }
    }
}
