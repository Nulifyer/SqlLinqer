namespace SqlLinqer.Modeling
{
    internal interface ISqlArgument
    {
        bool IsSpecified { get; }
        object UntypedValue { get; }
    }

    /// <summary>
    /// Distinguishes an omitted procedure argument from a supplied value, including SQL NULL. The default struct value omits the SQL assignment.
    /// Omission allows a procedure default to apply.
    /// </summary>
    /// <typeparam name="T">The declared input value type.</typeparam>
    /// <example>
    /// <code>
    /// SqlArgument&lt;string&gt; useDefault = SqlArgument&lt;string&gt;.Omitted;
    /// SqlArgument&lt;string&gt; passNull = new SqlArgument&lt;string&gt;(null);
    /// SqlArgument&lt;string&gt; passValue = "Alice";
    /// </code>
    /// </example>
    public readonly struct SqlArgument<T> : ISqlArgument
    {
        /// <summary>
        /// True when a value was supplied, including explicit null. False means the procedure argument is omitted.
        /// </summary>
        public bool IsSpecified { get; }
        /// <summary>
        /// The supplied CLR value. Check IsSpecified because an omitted argument also holds default(T).
        /// </summary>
        public T Value { get; }
        object ISqlArgument.UntypedValue => Value;

        /// <summary>
        /// Supplies the procedure argument, including SQL NULL when T permits null.
        /// </summary>
        /// <param name="value">The input value. Null is distinct from an omitted argument.</param>
        public SqlArgument(T value) { Value = value; IsSpecified = true; }
        /// <summary>
        /// Omits this argument from the SQL call so the procedure can apply its parameter default.
        /// </summary>
        public static SqlArgument<T> Omitted => default;
        /// <summary>
        /// Supplies an argument value through implicit conversion. A null value remains specified rather than omitted.
        /// </summary>
        /// <param name="value">The input value to supply.</param>
        /// <returns>A specified argument holding this value.</returns>
        public static implicit operator SqlArgument<T>(T value) => new SqlArgument<T>(value);
    }
}
