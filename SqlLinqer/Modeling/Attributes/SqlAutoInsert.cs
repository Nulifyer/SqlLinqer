using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Includes a mapped member in InsertAuto, subject to database-generated primary-key exclusion.
    /// On a model class, sets the member default; member attributes can override it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoInsert : Attribute
    {
        /// <summary>
        /// Includes a mapped member in InsertAuto, subject to database-generated primary-key exclusion.
        /// </summary>
        public SqlAutoInsert()
        {

        }
    }
}
