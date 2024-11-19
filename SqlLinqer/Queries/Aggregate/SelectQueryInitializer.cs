using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Extensions.MemberExpressionExtensions;

namespace SqlLinqer.Queries.Typed
{
    public class SelectQueryInitializer<TBase>
        where TBase : class
    {
        protected readonly Table Table;

        public SelectQueryInitializer(string default_schema = null) : this(Table.GetCached<TBase>(default_schema)) { }
        public SelectQueryInitializer(Table table)
        {
            Table = table;
        }

        private IEnumerable<(MemberInfo member, Expression argument)> GetMembersAndArguments(Expression expression)
        {
            switch (expression)
            {
                case NewExpression newExpression:
                    return newExpression.Members
                        .Zip(newExpression.Arguments, (member, argument) => (member, argument));
                case MemberInitExpression memberInitExpression:
                    return memberInitExpression.Bindings
                        .OfType<MemberAssignment>()
                        .Select(x => (x.Member, x.Expression));
                case LambdaExpression lambdaExpression:
                    return GetMembersAndArguments(lambdaExpression.Body);
                default:
                    throw new System.ArgumentException($"Invalid new expresion. The expression should be in the format of '() => new {{}}'", nameof(expression));
            }
        }

        public SelectAnonymousQuery<TBase, TReturn> BuildQuery<TReturn>(Expression<Func<SelectQueryBuilder<TBase>, TReturn>> expression)
            where TReturn : class
        {
            var builder = new SelectQueryBuilder<TBase, TReturn>(Table);

            var info = GetMembersAndArguments(expression);

            foreach (var (member, argument) in info)
            {
                var argument2 = argument;
                switch (argument)
                {
                    case UnaryExpression unary_expression:
                        if (unary_expression.Operand is MethodCallExpression call_expression)
                            argument2 = call_expression;
                        break;
                }
                switch (argument2)
                {
                    case MemberExpression member_expression:
                        builder.Select(member, member_expression, null);
                        break;
                    case MethodCallExpression call_expression:
                        var statement = builder.GetSelectStatement(member, call_expression);
                        if (statement != null)
                            builder.Query.Selects.AddStatement(statement);
                        break;
                    default:
                        throw new System.NotSupportedException($"The expression '{argument}' is not supported by '{nameof(SelectQueryInitializer<TBase>)}'.");
                }
            }

            return builder.Query;
        }


    }
}