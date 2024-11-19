using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines the target property or field to be always be updated on default.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlUpdateDefaultAlways : Attribute
    {
        /// <summary>
        /// Defines the target property or field to be always be updated on default.
        /// </summary>
        public SqlUpdateDefaultAlways()
        {

        }
    }
}
