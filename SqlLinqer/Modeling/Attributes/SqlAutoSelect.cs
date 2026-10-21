using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Includes a mapped member in SelectAuto. Use it to opt a relationship into automatic loading.
    /// On a model class, sets the member default; member attributes can override it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlAutoSelect : Attribute
    {
        /// <summary>
        /// Includes a mapped member in SelectAuto. Use it to opt a relationship into automatic loading.
        /// </summary>
        public SqlAutoSelect()
        {

        }
    }
}
