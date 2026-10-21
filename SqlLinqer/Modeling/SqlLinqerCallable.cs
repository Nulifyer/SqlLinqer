using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Modeling
{
    /// <summary>A typed stored procedure definition. Nested argument and result classes are optional.</summary>
    /// <typeparam name="TCallable">The concrete callable definition with a SqlProcedure attribute.</typeparam>
    /// <typeparam name="TArgs">The argument model.</typeparam>
    /// <typeparam name="TResult">The result model with a public parameterless constructor.</typeparam>
    public abstract class SqlLinqerCallable<TCallable, TArgs, TResult>
        where TCallable : SqlLinqerCallable<TCallable, TArgs, TResult>
        where TArgs : class
        where TResult : class, new()
    {
        /// <summary>Creates an invocation and captures its argument values without executing SQL.</summary>
        public static CallableInvocation<TCallable, TArgs, TResult> Call(TArgs args)
            => new CallableInvocation<TCallable, TArgs, TResult>(args);
    }
}
