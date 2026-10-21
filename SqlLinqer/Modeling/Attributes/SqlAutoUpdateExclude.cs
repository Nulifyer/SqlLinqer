using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Omits a mapped member from UpdateAuto without affecting explicit Update or UpdateNonDefaults assignments.
    /// On a model class, sets the member default; member attributes can override it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoUpdateExclude : Attribute
    {
        /// <summary>
        /// Omits a mapped member from UpdateAuto without affecting explicit Update or UpdateNonDefaults assignments.
        /// </summary>
        public SqlAutoUpdateExclude()
        {

        }
    }
}
