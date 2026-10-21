using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Includes the target property or field in UpdateNonDefaults even when its value is null.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlUpdateDefaultAlways : Attribute
    {
        /// <summary>
        /// Creates an attribute that includes null values in UpdateNonDefaults assignments.
        /// </summary>
        public SqlUpdateDefaultAlways()
        {

        }
    }
}
