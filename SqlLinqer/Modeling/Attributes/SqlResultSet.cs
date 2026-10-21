using System;

namespace SqlLinqer.Modeling
{
    /// <summary>Maps a result collection to a zero-based tabular result-set index.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SqlResultSet : Attribute
    {
        /// <summary>The zero-based result-set index.</summary>
        public int Index { get; }
        /// <summary>Defines the result-set index.</summary>
        public SqlResultSet(int index) { Index = index; }
    }

    /// <summary>Maps an Int32 result member to the procedure's RETURN code.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SqlReturnValue : Attribute { }
}
