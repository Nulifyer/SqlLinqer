using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines the target class to have all columns selected on auto.
    /// Defines the target property or field to be selected on auto.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoSelect : Attribute
    {
        /// <summary>
        /// Defines the target class to have all columns selected on auto.
        /// Defines the target property or field to be selected on auto.
        /// </summary>
        public SqlAutoSelect()
        {

        }
    }
}
