using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Exceptions;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Where;
using System;

namespace SqlLinqer.Queries.Typed
{
    public class SelectQueryBuilder<TBase, TReturn>
        where TBase : class
        where TReturn : class
    {
        public readonly SelectAnonymousQuery<TBase, TReturn> Query;

        public SelectQueryBuilder(Table table)
        {
            Query = new SelectAnonymousQuery<TBase, TReturn>(table);
        }

        /* *******************************************************
         * Non-Functions
         * *******************************************************
         */

        public void Select(MemberInfo member, Expression expression)
        {
            _Select(member, expression, null);
        }

        public void Select(MemberInfo member, Expression expression, Expression[] sub_expressions)
        {
            _Select(member, expression, null);

            if (sub_expressions != null)
            {
                var base_path = expression.GetMemberPath();
                foreach (var expr in sub_expressions)
                {
                    _Select(member, expr, base_path);
                }
            }
        }

        private void _Select(MemberInfo member, Expression expression, IEnumerable<string> base_path)
        {
            IEnumerable<string> path = expression.GetMemberPath();
            if (base_path != null)
                path = base_path.Union(path);

            var rel = Query.Table.FindRelationshipFromPath(path);
            if (rel != null)
            {
                Query.SelectRelationship(rel, member.Name);
            }
            else
            {
                var col = Query.Table.FindColumnFromPath(path);
                if (col == null)
                    throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Query.Table, path);
                Query.SelectColumn(col, member.Name, enable_sub_queries: false);
            }
        }

        public void SelectCase(MemberInfo member, LambdaExpression expression)
        {
            var case_statement = Case(member, expression);
            Query.Selects.AddStatement(case_statement);
        }
        
        public CaseStatement Case(MemberInfo member, LambdaExpression expression)
        {
            var case_statement = new CaseStatement<TBase>(Query.Table, member.Name);
            expression.Compile().DynamicInvoke(case_statement);
            return case_statement;
        }
        
        /* *******************************************************
         * Single Column Functions
         * *******************************************************
         */

        public SelectFuncStatement ABS(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.ABS, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement DAY(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.DAY, member.Name, arg);
            return stm;
        }

        public void LEFT(MemberInfo member, Expression expression, ConstantExpression length) => SUBSTRING(member, expression, Expression.Constant(0), length);

        public SelectFuncStatement LOWER(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.LOWER, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement LTRIM(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.LTRIM, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement MONTH(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.MONTH, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement REVERSE(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.REVERSE, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement RIGHT(MemberInfo member, Expression expression, ConstantExpression length)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.RIGHT, member.Name, arg, length.Value);
            return stm;
        }

        public SelectFuncStatement RTRIM(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.RTRIM, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement SOUNDDEX(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.SOUNDDEX, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement SUBSTRING(MemberInfo member, Expression expression, ConstantExpression start, ConstantExpression lenth)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.SUBSTRING, member.Name, arg, start.Value, lenth.Value);
            return stm;
        }

        public SelectFuncStatement TRIM(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.TRIM, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement UPPER(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.UPPER, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement YEAR(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.YEAR, member.Name, arg);
            return stm;
        }

        /* *******************************************************
         * Multi Column Functions
         * *******************************************************
         */

        public SelectFuncStatement Coalesce(MemberInfo member, params Expression[] expressions)
        {
            var args = GetArgs(member, expressions);
            var stm = new SelectFuncStatement(SqlFunc.COALESCE, member.Name, args);
            return stm;
        }

        public SelectStatement Concat(MemberInfo member, Expression[] expressions)
        {
            var args = GetArgs(member, expressions);
            var stm = new SelectFuncStatement(SqlFunc.CONCAT, member.Name, args);
            return stm;
        }

        public SelectStatement ConcatWS(MemberInfo member, ConstantExpression separator, Expression[] expressions)
        {
            var args = new List<object>();
            args.Add(separator.Value);
            args.AddRange(GetArgs(member, expressions));
            var stm = new SelectFuncStatement(SqlFunc.CONCAT_WS, member.Name, args.ToArray());
            return stm;
        }

        public SelectStatement Join(MemberInfo member, ConstantExpression separator, Expression[] expressions) => ConcatWS(member, separator, expressions);

        /* *******************************************************
         * Aggregate Functions
         * *******************************************************
         */

        public SelectFuncStatement Avg(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.AVG, member.Name, arg);
            return stm;
        }

        public SelectFuncStatement Count(MemberInfo member) => Count(member, null, Expression.Constant(false));
        public SelectFuncStatement Count(MemberInfo member, Expression expression) => Count(member, expression, Expression.Constant(false));
        public SelectFuncStatement Count(MemberInfo member, Expression expression, ConstantExpression distinct)
        {
            object arg = expression != null ? GetArg(member, expression) : 1;
            var stm = new SelectDistinctFuncStatement(SqlFunc.COUNT, (bool)distinct.Value, member.Name, arg);
            return stm;
        }
        
        public SelectFuncStatement CountBig(MemberInfo member) => CountBig(member, null, Expression.Constant(false));
        public SelectFuncStatement CountBig(MemberInfo member, Expression expression) => CountBig(member, expression, Expression.Constant(false));
        public SelectFuncStatement CountBig(MemberInfo member, Expression expression, ConstantExpression distinct)
        {
            object arg = expression != null ? GetArg(member, expression) : 1;
            var stm = new SelectDistinctFuncStatement(SqlFunc.COUNT_BIG, (bool)distinct.Value, member.Name, arg);
            return stm;
        }
        
        public SelectFuncStatement Max(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.MAX, member.Name, arg);
            return stm;
        }
        
        public SelectFuncStatement Min(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.MIN, member.Name, arg);
            return stm;
        }
        
        public SelectFuncStatement StringAgg(MemberInfo member, params Expression[] expressions)
        {
            var args = GetArgs(member, expressions);
            var stm = new SelectFuncStatement(SqlFunc.STRING_AGG, member.Name, args);
            return stm;
        }

        public SelectFuncStatement Sum(MemberInfo member, Expression expression)
        {
            var arg = GetArg(member, expression);
            var stm = new SelectFuncStatement(SqlFunc.SUM, member.Name, arg);
            return stm;
        }

        /* *******************************************************
         * Processing Functions
         * *******************************************************
         */

        public SelectStatement GetSelectStatement(MemberInfo member, MethodCallExpression call_expression)
        {
            var args = new List<object>();
            args.Add(member);
            args.AddRange(ProcessArgs(call_expression.Arguments));
            var args_array = args.ToArray();

            var arg_types = args_array.Select(x => x.GetType()).ToArray();

            var method = this.GetType().GetMethod(call_expression.Method.Name, arg_types);
            if (method == null)
                throw new System.FormatException($"Invalid method target from expression '{call_expression}'.");
            return method.Invoke(this, args_array) as SelectStatement;
        }

        /* *******************************************************
         * Private Functions
         * *******************************************************
         */

        private static IEnumerable<object> ProcessArgs(IEnumerable<object> args)
        {
            return args.Select(a =>
            {
                switch (a)
                {
                    case NewArrayExpression arrayExpression:
                        return arrayExpression.Expressions.ToArray();
                    default:
                        return a;
                }
            });
        }

        private object GetArg(MemberInfo member, Expression expression)
        {
            switch (expression)
            {
                case LambdaExpression lambdaExpression:
                    return GetArg(member, lambdaExpression.Body);
                case UnaryExpression unaryExpression:
                    return GetArg(member, unaryExpression.Operand);
                case ConstantExpression constantExpression:
                    return constantExpression.Value;
                case MethodCallExpression callExpression:
                    return GetSelectStatement(member, callExpression);
                default:
                    return GetColumn(expression);
            }
        }
        private object[] GetArgs(MemberInfo member, Expression[] expression)
        {
            return expression.Select(x => GetArg(member, x)).ToArray();
        }
        private Column GetColumn(Expression expression)
        {
            var path = expression.GetMemberPath();
            var col = Query.Table.FindColumnFromPath(path);
            if (col == null)
                throw new SqlPathNotFoundException(Query.Table, path);
            return col;
        }
        private IEnumerable<Column> GetColumns(Expression[] expressions)
        {
            return expressions.Select(x => GetColumn(x));
        }
    }
}