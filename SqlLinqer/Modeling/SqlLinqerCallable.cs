using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines a SQL Server or Azure SQL procedure with typed inputs, tabular result sets, output parameters, and an optional return code.
    /// Result members require SqlResultSet, SqlOutputParameter, or SqlReturnValue unless excluded with SqlIgnore. Row mapping uses SQL attributes independently of API JSON settings.
    /// </summary>
    /// <typeparam name="TCallable">The concrete definition carrying SqlProcedure.</typeparam>
    /// <typeparam name="TArgs">The input model. Public fields and readable properties become parameters unless marked SqlIgnore.</typeparam>
    /// <typeparam name="TResult">The writable result model with a public parameterless constructor.</typeparam>
    public abstract class SqlLinqerCallable<TCallable, TArgs, TResult>
        where TCallable : SqlLinqerCallable<TCallable, TArgs, TResult>
        where TArgs : class
        where TResult : class, new()
    {
        /// <summary>
        /// Reads the argument member values once and creates a procedure invocation without executing SQL.
        /// Later member assignments do not change the invocation. Reference values, including arrays and DataTables, are not deep-copied and must remain unchanged until execution finishes. Invalid definitions or arguments throw here.
        /// </summary>
        /// <param name="args">The input model. SqlArgument members distinguish omitted parameters from SQL NULL.</param>
        /// <returns>An invocation whose argument member assignments are fixed at construction.</returns>
        public static CallableInvocation<TCallable, TArgs, TResult> Call(TArgs args)
            => new CallableInvocation<TCallable, TArgs, TResult>(args);
    }
}
