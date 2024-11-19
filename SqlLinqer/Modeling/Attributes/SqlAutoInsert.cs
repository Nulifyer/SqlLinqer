using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines the target class to have all columns inserted on auto.
    /// Defines the target property or field to be inserted on auto.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoInsert : Attribute
    {
        /// <summary>
        /// Defines the target class to have all columns inserted on auto.
        /// Defines the target property or field to be inserted on auto.
        /// </summary>
        public SqlAutoInsert()
        {

        }
    }
}
