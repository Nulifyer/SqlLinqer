using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Where;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Defines SQL projection marker methods consumed by BuildQuery. Calls belong inside a projection expression, rather than ordinary application execution.
    /// Projection member names determine result aliases. Database function support depends on the dialect.
    /// </summary>
    /// <typeparam name="TBase">The mapped source model.</typeparam>
    public abstract class SelectQueryBuilder<TBase>
        where TBase : class
    {
        /// <summary>Creates a projection expression builder.</summary>
        public SelectQueryBuilder() { }

        /* *******************************************************
         * Non-Functions
         * *******************************************************
         */

        /// <summary>Projects a model member, optionally selecting specified nested members.</summary>
        public abstract T Select<T>(Expression<Func<TBase, T>> expression);

        /// <summary>Projects a related model with the specified nested members.</summary>
        public abstract T Select<T>(Expression<Func<TBase, T>> expression, params Expression<Func<T, object>>[] sub_expressions) where T : class;

        /* *******************************************************
         * Single Column Functions
         * *******************************************************
         */

        /// <summary>Projects the absolute value.</summary>
        public abstract TProperty ABS<TProperty>(Expression<Func<TBase, TProperty>> expression);

        /// <summary>Projects the day of the month.</summary>
        public abstract int DAY(Expression<Func<TBase, DateTime?>> expression);

        /// <summary>Projects the day of the month.</summary>
        public abstract int DAY(Expression<Func<TBase, DateTimeOffset?>> expression);

        /// <summary>Projects the leading characters.</summary>
        public abstract string LEFT(Expression<Func<TBase, string>> expression, int length);

        /// <summary>Projects lowercase text.</summary>
        public abstract string LOWER(Expression<Func<TBase, string>> expression);

        /// <summary>Projects text without leading spaces.</summary>
        public abstract string LTRIM(Expression<Func<TBase, string>> expression);

        /// <summary>Projects the month.</summary>
        public abstract int MONTH(Expression<Func<TBase, DateTime?>> expression);

        /// <summary>Projects the month.</summary>
        public abstract int MONTH(Expression<Func<TBase, DateTimeOffset?>> expression);

        /// <summary>Projects reversed text.</summary>
        public abstract string REVERSE(Expression<Func<TBase, string>> expression);

        /// <summary>Projects the trailing characters.</summary>
        public abstract string RIGHT(Expression<Func<TBase, string>> expression, int length);

        /// <summary>Projects text without trailing spaces.</summary>
        public abstract string RTRIM(Expression<Func<TBase, string>> expression);

        /// <summary>Projects the SOUNDEX phonetic code.</summary>
        public abstract string SOUNDDEX(Expression<Func<TBase, string>> expression);

        /// <summary>Projects a substring using the database's position rules.</summary>
        public abstract string SUBSTRING(Expression<Func<TBase, string>> expression, int start, int lenth);

        /// <summary>Projects trimmed text.</summary>
        public abstract string TRIM(Expression<Func<TBase, string>> expression);

        /// <summary>Projects uppercase text.</summary>
        public abstract string UPPER(Expression<Func<TBase, string>> expression);

        /// <summary>Projects the year.</summary>
        public abstract int YEAR(Expression<Func<TBase, DateTime?>> expression);

        /// <summary>Projects the year.</summary>
        public abstract int YEAR(Expression<Func<TBase, DateTimeOffset?>> expression);

        /* *******************************************************
         * Multi Column Functions
         * *******************************************************
         */

        /// <summary>Projects the first non-null expression.</summary>
        public abstract string Coalesce(params Expression<Func<TBase, object>>[] expressions);

        /// <summary>Projects the first non-null expression.</summary>
        public abstract string Coalesce<TProperty>(params Expression<Func<TBase, TProperty>>[] expressions);

        /// <summary>Projects concatenated expressions.</summary>
        public abstract string Concat(params Expression<Func<TBase, object>>[] expressions);

        /// <summary>Projects expressions concatenated with a separator.</summary>
        public abstract string ConcatWS(string separator, params Expression<Func<TBase, object>>[] expressions);

        /// <summary>Projects expressions concatenated with a separator.</summary>
        public abstract string Join(string separator, params Expression<Func<TBase, object>>[] expressions);

        /* *******************************************************
         * Aggregate Functions
         * *******************************************************
         */

        /// <summary>
        /// Projects the SQL average. The legacy marker returns int; choose a compatible projection member type for the database result.
        /// A projection conversion selects the CLR result type without changing SQL aggregate arithmetic.
        /// </summary>
        /// <typeparam name="TProperty">The numeric source member type.</typeparam>
        /// <param name="expression">The numeric mapped member or relationship member to aggregate.</param>
        /// <returns>A marker for the aggregate result inside the projection.</returns>
        public abstract int Avg<TProperty>(Expression<Func<TBase, TProperty>> expression);

        /// <summary>
        /// Projects a row count.
        /// Selecting non-aggregate members also groups by those expressions.
        /// </summary>
        /// <returns>A SQL count marker for the projection.</returns>
        public abstract int Count();
        /// <summary>
        /// Projects a count of non-null expression values.
        /// Selecting non-aggregate members also groups by those expressions.
        /// </summary>
        /// <typeparam name="TProperty">The source expression type.</typeparam>
        /// <param name="expression">The mapped expression whose non-null results are counted.</param>
        /// <returns>A SQL count marker for the projection.</returns>
        public abstract int Count<TProperty>(Expression<Func<TBase, TProperty>> expression);
        /// <summary>
        /// Projects a count of non-null expression values, optionally distinct.
        /// Selecting non-aggregate members also groups by those expressions.
        /// </summary>
        /// <typeparam name="TProperty">The source expression type.</typeparam>
        /// <param name="expression">The mapped expression whose non-null results are counted.</param>
        /// <param name="distinct">True to count distinct non-null expression values.</param>
        /// <returns>A SQL count marker for the projection.</returns>
        public abstract int Count<TProperty>(Expression<Func<TBase, TProperty>> expression, bool distinct);

        /// <summary>
        /// Projects a 64-bit row count.
        /// Selecting non-aggregate members also groups by those expressions.
        /// </summary>
        /// <returns>A SQL count marker for the projection.</returns>
        public abstract long CountBig();
        /// <summary>
        /// Projects a 64-bit count of non-null expression values.
        /// Selecting non-aggregate members also groups by those expressions.
        /// </summary>
        /// <typeparam name="TProperty">The source expression type.</typeparam>
        /// <param name="expression">The mapped expression whose non-null results are counted.</param>
        /// <returns>A SQL count marker for the projection.</returns>
        public abstract long CountBig<TProperty>(Expression<Func<TBase, TProperty>> expression);
        /// <summary>
        /// Projects a 64-bit count of non-null expression values, optionally distinct.
        /// Selecting non-aggregate members also groups by those expressions.
        /// </summary>
        /// <typeparam name="TProperty">The source expression type.</typeparam>
        /// <param name="expression">The mapped expression whose non-null results are counted.</param>
        /// <param name="distinct">True to count distinct non-null expression values.</param>
        /// <returns>A SQL count marker for the projection.</returns>
        public abstract long CountBig<TProperty>(Expression<Func<TBase, TProperty>> expression, bool distinct);

        /// <summary>Defines a CASE expression for the projected member.</summary>
        public abstract TReturn Case<TReturn>(Action<CaseStatement<TBase>> case_statement);

        /// <summary>Projects the maximum.</summary>
        public abstract TProperty Max<TProperty>(Expression<Func<TBase, TProperty>> expression);

        /// <summary>Projects the minimum.</summary>
        public abstract TProperty Min<TProperty>(Expression<Func<TBase, TProperty>> expression);

        /// <summary>Projects strings aggregated across rows.</summary>
        public abstract string StringAgg(params Expression<Func<TBase, object>>[] expressions);

        /// <summary>
        /// Projects the SQL sum. The legacy marker returns int; choose a compatible projection member type for the database result.
        /// A projection conversion selects the CLR result type without changing SQL aggregate arithmetic.
        /// </summary>
        /// <typeparam name="TProperty">The numeric source member type.</typeparam>
        /// <param name="expression">The numeric mapped member or relationship member to aggregate.</param>
        /// <returns>A marker for the aggregate result inside the projection.</returns>
        public abstract int Sum<TProperty>(Expression<Func<TBase, TProperty>> expression);
    }
}
