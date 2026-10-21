using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Includes a mapped non-primary-key member in UpdateAuto, including a null value.
    /// On a model class, sets the member default; member attributes can override it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoUpdate : Attribute
    {
        /// <summary>
        /// Includes a mapped non-primary-key member in UpdateAuto, including a null value.
        /// </summary>
        public SqlAutoUpdate()
        {

        }
    }
}
