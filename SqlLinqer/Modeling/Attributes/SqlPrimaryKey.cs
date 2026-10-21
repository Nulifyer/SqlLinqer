using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Marks the model's primary-key member. Database-generated keys are omitted from automatic inserts and all keys are excluded from automatic updates.
    /// Set dbGenerated to false for application-generated IDs. This mapping does not create a database key constraint.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlPrimaryKey : Attribute
    {
        /// <summary>
        /// Whether the database generates the primary key. Defaults to true and causes InsertAuto to omit the key column.
        /// </summary>
        public readonly bool DbGenerated;

        /// <summary>
        /// Marks a database-generated primary key, excluding it from automatic insert inputs.
        /// </summary>
        public SqlPrimaryKey()
        {
            DbGenerated = true;
        }

        /// <summary>
        /// Marks a primary key and chooses whether InsertAuto supplies its value.
        /// </summary>
        /// <param name="dbGenerated">True when the database generates the key. False includes the application's key value in automatic inserts.</param>
        public SqlPrimaryKey(bool dbGenerated)
        {
            DbGenerated = dbGenerated;
        }
    }
}
