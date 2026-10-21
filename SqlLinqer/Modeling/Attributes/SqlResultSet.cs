using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a writable result collection to a zero-based tabular result-set index. Indexes must be unique and contiguous from zero.
    /// Arrays, lists, and interfaces assignable from List are supported. Empty sets produce empty collections; missing or extra tabular sets are errors. Scalar rows require one column; object rows use SqlColumn and SqlIgnore. SQL NULL requires a nullable target.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SqlResultSet : Attribute
    {
        /// <summary>The zero-based result-set index.</summary>
        public int Index { get; }
        /// <summary>
        /// Selects the zero-based tabular result set loaded into this result collection.
        /// </summary>
        /// <param name="index">The result-set position among tabular sets, starting at zero. Declared indexes must be contiguous.</param>
        public SqlResultSet(int index) { Index = index; }
    }

    /// <summary>
    /// Maps one writable Int32 result member to the procedure RETURN code. A nonzero code does not automatically mark the response as an error.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SqlReturnValue : Attribute { }
}
