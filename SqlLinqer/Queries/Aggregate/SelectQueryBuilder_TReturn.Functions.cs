using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using SqlLinqer.Components.Select;

namespace SqlLinqer.Queries.Typed
{
    public partial class SelectQueryBuilder<TBase, TReturn>
    {

        /// <summary>Creates a SQL expression for the absolute value.</summary>
        public SelectFuncStatement ABS(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.ABS, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for the day of the month.</summary>
        public SelectFuncStatement DAY(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.DAY, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for the leading characters.</summary>
        public void LEFT(MemberInfo member, Expression expression, ConstantExpression length) => SUBSTRING(member, expression, Expression.Constant(0), length);

        /// <summary>Creates a SQL expression for lowercase text.</summary>
        public SelectFuncStatement LOWER(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.LOWER, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for text without leading spaces.</summary>
        public SelectFuncStatement LTRIM(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.LTRIM, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for the month.</summary>
        public SelectFuncStatement MONTH(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.MONTH, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for reversed text.</summary>
        public SelectFuncStatement REVERSE(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.REVERSE, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for the trailing characters.</summary>
        public SelectFuncStatement RIGHT(MemberInfo member, Expression expression, ConstantExpression length)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.RIGHT, member.Name, arg, length.Value);
            return stm;
        }

        /// <summary>Creates a SQL expression for text without trailing spaces.</summary>
        public SelectFuncStatement RTRIM(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.RTRIM, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for the SOUNDEX phonetic code.</summary>
        public SelectFuncStatement SOUNDDEX(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.SOUNDDEX, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for a substring using the database's position rules.</summary>
        public SelectFuncStatement SUBSTRING(MemberInfo member, Expression expression, ConstantExpression start, ConstantExpression lenth)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.SUBSTRING, member.Name, arg, start.Value, lenth.Value);
            return stm;
        }

        /// <summary>Creates a SQL expression for trimmed text.</summary>
        public SelectFuncStatement TRIM(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.TRIM, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for uppercase text.</summary>
        public SelectFuncStatement UPPER(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.UPPER, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for the year.</summary>
        public SelectFuncStatement YEAR(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.YEAR, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for the first non-null expression.</summary>
        public SelectFuncStatement Coalesce(MemberInfo member, params Expression[] expressions)
        {
            var args = GetArgs(member, expressions);
            var stm = new SelectFuncStatement(SqlFunc.COALESCE, member.Name, args);
            return stm;
        }

        /// <summary>Creates a SQL expression for concatenated expressions.</summary>
        public SelectStatement Concat(MemberInfo member, Expression[] expressions)
        {
            var args = GetArgs(member, expressions);
            var stm = new SelectFuncStatement(SqlFunc.CONCAT, member.Name, args);
            return stm;
        }

        /// <summary>Creates a SQL expression for expressions concatenated with a separator.</summary>
        public SelectStatement ConcatWS(MemberInfo member, ConstantExpression separator, Expression[] expressions)
        {
            var args = new List<object>();
            args.Add(separator.Value);
            args.AddRange(GetArgs(member, expressions));
            var stm = new SelectFuncStatement(SqlFunc.CONCAT_WS, member.Name, args.ToArray());
            return stm;
        }

        /// <summary>Creates a SQL expression for expressions concatenated with a separator.</summary>
        public SelectStatement Join(MemberInfo member, ConstantExpression separator, Expression[] expressions) => ConcatWS(member, separator, expressions);

        /// <summary>Creates a SQL expression for the average.</summary>
        public SelectFuncStatement Avg(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.AVG, member.Name, arg);
            return stm;
        }

        /// <summary>Creates a SQL expression for the non-null expression count.</summary>
        public SelectFuncStatement Count(MemberInfo member) => Count(member, null, Expression.Constant(false));

        /// <summary>Creates a SQL expression for the non-null expression count.</summary>
        public SelectFuncStatement Count(MemberInfo member, Expression expression) => Count(member, expression, Expression.Constant(false));

        /// <summary>Creates a SQL expression for the non-null expression count, optionally counting distinct values.</summary>
        public SelectFuncStatement Count(MemberInfo member, Expression expression, ConstantExpression distinct)
        {
            object arg = expression != null ? GetArg(member, expression) : 1;
            var stm = new SelectDistinctFuncStatement(SqlFunc.COUNT, (bool)distinct.Value, member.Name, arg);
            return stm;
        }


        /// <summary>Creates a SQL expression for a 64-bit non-null expression count.</summary>
        public SelectFuncStatement CountBig(MemberInfo member) => CountBig(member, null, Expression.Constant(false));

        /// <summary>Creates a SQL expression for a 64-bit non-null expression count.</summary>
        public SelectFuncStatement CountBig(MemberInfo member, Expression expression) => CountBig(member, expression, Expression.Constant(false));

        /// <summary>Creates a SQL expression for a 64-bit non-null expression count, optionally counting distinct values.</summary>
        public SelectFuncStatement CountBig(MemberInfo member, Expression expression, ConstantExpression distinct)
        {
            object arg = expression != null ? GetArg(member, expression) : 1;
            var stm = new SelectDistinctFuncStatement(SqlFunc.COUNT_BIG, (bool)distinct.Value, member.Name, arg);
            return stm;
        }


        /// <summary>Creates a SQL expression for the maximum.</summary>
        public SelectFuncStatement Max(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.MAX, member.Name, arg);
            return stm;
        }


        /// <summary>Creates a SQL expression for the minimum.</summary>
        public SelectFuncStatement Min(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.MIN, member.Name, arg);
            return stm;
        }


        /// <summary>Creates a SQL expression for strings aggregated across rows.</summary>
        public SelectFuncStatement StringAgg(MemberInfo member, params Expression[] expressions)
        {
            var args = GetArgs(member, expressions);
            var stm = new SelectFuncStatement(SqlFunc.STRING_AGG, member.Name, args);
            return stm;
        }

        /// <summary>Creates a SQL expression for the sum.</summary>
        public SelectFuncStatement Sum(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.SUM, member.Name, arg);
            return stm;
        }
    }
}
