using System;

namespace SqlLinqer
{
    /// <summary>
    /// Tells SQLLinqer to ignore this property or field
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SQLIgnore : Attribute
    {
        /// <summary>
        /// Tells SQLLinqer to ignore this property or field
        /// </summary>
        public SQLIgnore() { }
    }
}
