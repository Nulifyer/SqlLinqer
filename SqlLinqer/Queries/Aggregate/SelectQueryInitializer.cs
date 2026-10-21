using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.MemberExpressionExtensions;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Starts a SELECT projection over a mapped model. BuildQuery defines the returned members and SQL expressions.
    /// </summary>
    /// <typeparam name="TBase">The existing mapped root model.</typeparam>
    public class SelectQueryInitializer<TBase>
        where TBase : class
    {
        /// <summary>The root table mapping used by the projection.</summary>
        protected readonly Table Table;
        internal BaseConnector BoundConnector { get; set; }

        /// <summary>Creates a projection initializer from a root mapping or cached model mapping.</summary>
        public SelectQueryInitializer(string default_schema = null) : this(Table.GetCached<TBase>(default_schema)) { }
        /// <summary>Creates a projection initializer from a root mapping or cached model mapping.</summary>
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

        /// <summary>
        /// Translates an anonymous-object or member-initializer projection into SQL. Selected member names become output aliases.
        /// SQL marker calls are translated rather than evaluated. The returned query filters with TBase expressions and orders with TReturn expressions.
        /// </summary>
        /// <typeparam name="TReturn">The anonymous or declared result model type.</typeparam>
        /// <param name="expression">A projection that constructs TReturn from SQL marker methods such as Select, Count, and Sum.</param>
        /// <returns>A mutable projected SELECT query retaining the initializer's connector and root mapping.</returns>
        /// <example>
        /// <code>
        /// var query = User.BeginSelectBuilder(connector)
        ///     .BuildQuery(x =&gt; new { Id = x.Select(p =&gt; p.Id), Count = x.Count() })
        ///     .OrderBy(x =&gt; x.Id, SqlDir.ASC);
        /// </code>
        /// </example>
        public SelectAnonymousQuery<TBase, TReturn> BuildQuery<TReturn>(Expression<Func<SelectQueryBuilder<TBase>, TReturn>> expression)
            where TReturn : class
        {
            var builder = new SelectQueryBuilder<TBase, TReturn>(Table);
            builder.Query.BoundConnector = BoundConnector;

            var info = GetMembersAndArguments(expression);

            foreach (var (member, argument) in info)
            {
                var argument2 = argument;
                while (argument2 is UnaryExpression conversion
                    && (conversion.NodeType == ExpressionType.Convert || conversion.NodeType == ExpressionType.ConvertChecked))
                    argument2 = conversion.Operand;
                switch (argument2)
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
