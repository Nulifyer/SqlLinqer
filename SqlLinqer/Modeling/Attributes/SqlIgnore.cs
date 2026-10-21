using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Excludes a CLR member from SQL mappings while leaving its normal API serialization rules unchanged.
    /// Use SqlAutoSelectExclude to exclude a mapped member only from automatic selects.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SqlIgnore : Attribute
    {
        /// <summary>
        /// Excludes the annotated member from SQL mapping rather than from API JSON serialization.
        /// </summary>
        public SqlIgnore() { }
    }
}
