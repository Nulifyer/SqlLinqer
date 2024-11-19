using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines the target class to have all columns updated on auto.
    /// Defines the target property or field to be updated on auto.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoUpdate : Attribute
    {
        /// <summary>
        /// Defines the target class to have all columns updated on auto.
        /// Defines the target property or field to be updated on auto.
        /// </summary>
        public SqlAutoUpdate()
        {

        }
    }
}
