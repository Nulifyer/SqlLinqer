using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines the target class to NOT have all columns updated on auto.
    /// Defines the target property or field to NOT be updated on auto.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoUpdateExclude : Attribute
    {
        /// <summary>
        /// Defines the target class to NOT have all columns updated on auto.
        /// Defines the target property or field to NOT be updated on auto.
        /// </summary>
        public SqlAutoUpdateExclude()
        {

        }
    }
}
