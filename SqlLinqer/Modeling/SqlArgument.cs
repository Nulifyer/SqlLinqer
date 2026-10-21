namespace SqlLinqer.Modeling
{
    internal interface ISqlArgument
    {
        bool IsSpecified { get; }
        object UntypedValue { get; }
    }

    /// <summary>An optional SQL argument. The default value omits the parameter and uses its database default.</summary>
    public readonly struct SqlArgument<T> : ISqlArgument
    {
        /// <summary>Whether the argument was supplied, including an explicit null.</summary>
        public bool IsSpecified { get; }
        /// <summary>The supplied value. Check IsSpecified to distinguish omission.</summary>
        public T Value { get; }
        object ISqlArgument.UntypedValue => Value;

        /// <summary>Supplies a value, including SQL NULL when T permits null.</summary>
        public SqlArgument(T value) { Value = value; IsSpecified = true; }
        /// <summary>Creates an omitted argument.</summary>
        public static SqlArgument<T> Omitted => default;
        /// <summary>Supplies an argument value.</summary>
        public static implicit operator SqlArgument<T>(T value) => new SqlArgument<T>(value);
    }
}
