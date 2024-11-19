using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Tells SqlLinqer to ignore this property or field
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlIgnore : Attribute
    {
        /// <summary>
        /// Tells SqlLinqer to ignore this property or field
        /// </summary>
        public SqlIgnore() { }
    }
}
