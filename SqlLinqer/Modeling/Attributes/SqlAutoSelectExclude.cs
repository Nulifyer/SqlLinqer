using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Omits a mapped member from SelectAuto while preserving explicit Select and SelectRootColumns access.
    /// On a model class, sets the member default; member attributes can override it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoSelectExclude : Attribute
    {
        /// <summary>
        /// Omits a mapped member from SelectAuto while preserving explicit Select and SelectRootColumns access.
        /// </summary>
        public SqlAutoSelectExclude()
        {

        }
    }
}
