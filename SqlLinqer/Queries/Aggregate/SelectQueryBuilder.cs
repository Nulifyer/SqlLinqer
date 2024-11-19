using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Where;

namespace SqlLinqer.Queries.Typed
{
    public abstract class SelectQueryBuilder<TBase>
        where TBase : class
    {
        public SelectQueryBuilder() {}

        /* *******************************************************
         * Non-Functions
         * *******************************************************
         */

        public abstract T Select<T>(Expression<Func<TBase, T>> expression);

        public abstract T Select<T>(Expression<Func<TBase, T>> expression, params Expression<Func<T, object>>[] sub_expressions) where T : class;

        /* *******************************************************
         * Single Column Functions
         * *******************************************************
         */

        public abstract TProperty ABS<TProperty>(Expression<Func<TBase, TProperty>> expression);

        public abstract int DAY(Expression<Func<TBase, DateTime?>> expression);

        public abstract int DAY(Expression<Func<TBase, DateTimeOffset?>> expression);

        public abstract string LEFT(Expression<Func<TBase, string>> expression, int length);

        public abstract string LOWER(Expression<Func<TBase, string>> expression);

        public abstract string LTRIM(Expression<Func<TBase, string>> expression);

        public abstract int MONTH(Expression<Func<TBase, DateTime?>> expression);

        public abstract int MONTH(Expression<Func<TBase, DateTimeOffset?>> expression);

        public abstract string REVERSE(Expression<Func<TBase, string>> expression);

        public abstract string RIGHT(Expression<Func<TBase, string>> expression, int length);

        public abstract string RTRIM(Expression<Func<TBase, string>> expression);

        public abstract string SOUNDDEX(Expression<Func<TBase, string>> expression);

        public abstract string SUBSTRING(Expression<Func<TBase, string>> expression, int start, int lenth);

        public abstract string TRIM(Expression<Func<TBase, string>> expression);

        public abstract string UPPER(Expression<Func<TBase, string>> expression);

        public abstract int YEAR(Expression<Func<TBase, DateTime?>> expression);

        public abstract int YEAR(Expression<Func<TBase, DateTimeOffset?>> expression);

        /* *******************************************************
         * Multi Column Functions
         * *******************************************************
         */

        public abstract string Coalesce(params Expression<Func<TBase, object>>[] expressions);

        public abstract string Coalesce<TProperty>(params Expression<Func<TBase, TProperty>>[] expressions);

        public abstract string Concat(params Expression<Func<TBase, object>>[] expressions);

        public abstract string ConcatWS(string separator, params Expression<Func<TBase, object>>[] expressions);

        public abstract string Join(string separator, params Expression<Func<TBase, object>>[] expressions);

        /* *******************************************************
         * Aggregate Functions
         * *******************************************************
         */

        public abstract int Avg<TProperty>(Expression<Func<TBase, TProperty>> expression);

        public abstract int Count();
        public abstract int Count<TProperty>(Expression<Func<TBase, TProperty>> expression);
        public abstract int Count<TProperty>(Expression<Func<TBase, TProperty>> expression, bool distinct);

        public abstract long CountBig();
        public abstract long CountBig<TProperty>(Expression<Func<TBase, TProperty>> expression);
        public abstract long CountBig<TProperty>(Expression<Func<TBase, TProperty>> expression, bool distinct);

        public abstract TReturn Case<TReturn>(Action<CaseStatement<TBase>> case_statement);

        public abstract TProperty Max<TProperty>(Expression<Func<TBase, TProperty>> expression);

        public abstract TProperty Min<TProperty>(Expression<Func<TBase, TProperty>> expression);

        public abstract string StringAgg(params Expression<Func<TBase, object>>[] expressions);

        public abstract int Sum<TProperty>(Expression<Func<TBase, TProperty>> expression);
    }
}