using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Used to define a column name that is different from the property/field name
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlColumn : Attribute
    {
        /// <summary>
        /// The database name for this property/field
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The type in the database
        /// </summary>
        public readonly System.Data.DbType? DbType;

        /// <summary>
        /// Used to define a column name that is different from the property/field name
        /// </summary>
        /// <param name="name">The database column name</param>
        public SqlColumn(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Used to define a column name that is different from the property/field name
        /// </summary>
        /// <param name="name">The database column name</param>
        /// <param name="dbType">The type in the database</param>
        public SqlColumn(string name, System.Data.DbType dbType)
            : this(name)
        {
            DbType = dbType;
        }
    }
}
