using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;

namespace SqlLinqer.Extensions.MemberExpressionExtensions
{
    /// <summary>
    /// A set of expression extensions
    /// </summary>
    public static class MemberExpressionExtensions
    {
        /// <summary>
        /// Gets the list of members that were accessed in the member expression
        /// </summary>
        /// <param name="expression">The current expression</param>
        public static List<string> GetMemberPath(this Expression expression)
        {
            var path = new List<string>();
            ExtractMemberPath(expression, path);
            path.Reverse(); // Reverse the list to get the correct order
            return path;
        }

        private static void ExtractMemberPath(Expression expression, List<string> path)
        {
            switch (expression)
            {
                case UnaryExpression unaryExpression:
                    ExtractMemberPath(unaryExpression.Operand, path);
                    break;

                case MemberExpression memberExpression:
                    path.Add(memberExpression.Member.Name);
                    ExtractMemberPath(memberExpression.Expression, path);
                    break;

                case LambdaExpression lambdaExpression:
                    ExtractMemberPath(lambdaExpression.Body, path);
                    break;

                case ParameterExpression parameterExpression:
                    // Do nothing, as we've reached the end of the member access chain
                    break;

                case MethodCallExpression callExpression:
                    var expressions = callExpression.Arguments
                        .Where(x => x is Expression)
                        .Reverse();
                    foreach (var exp in expressions)
                    {
                        ExtractMemberPath(exp, path);
                    }
                    break;
                    
                case BinaryExpression binaryExpression:
                    ExtractMemberPath(binaryExpression.Left, path);
                    break;

                default:
                    throw new ArgumentException("Expression type not supported.");
            }
        }
    }
}