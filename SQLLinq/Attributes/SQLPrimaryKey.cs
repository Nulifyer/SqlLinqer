using System;

namespace SqlLinqer
{
    /// <summary>
    /// Defines a field/property as the primary key
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SQLPrimaryKey : Attribute
    {
        /// <summary>
        /// Primary key is generated by the database
        /// </summary>
        public bool DBGenerated { get; private set; }

        /// <summary>
        /// Defines a field/property as the primary key
        /// </summary>
        /// <param name="dbGenerated">If the database generates the primary key</param>
        public SQLPrimaryKey(bool dbGenerated = false)
        {
            DBGenerated = dbGenerated;
        }
    }
}
