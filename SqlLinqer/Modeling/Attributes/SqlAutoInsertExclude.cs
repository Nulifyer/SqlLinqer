using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Omits a mapped member from InsertAuto without removing its SQL mapping.
    /// On a model class, sets the member default; member attributes can override it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoInsertExclude : Attribute
    {
        /// <summary>
        /// Omits a mapped member from InsertAuto without removing its SQL mapping.
        /// </summary>
        public SqlAutoInsertExclude()
        {

        }
    }
}
