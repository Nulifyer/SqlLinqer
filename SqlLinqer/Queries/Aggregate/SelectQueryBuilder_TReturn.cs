using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Exceptions;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Extensions.MemberExpressionExtensions;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>Translates projected members and SQL function expressions into SELECT statements.</summary>
    public partial class SelectQueryBuilder<TBase, TReturn>
        where TBase : class
        where TReturn : class
    {
        /// <summary>The SELECT query populated by translated projection expressions.</summary>
        public readonly SelectAnonymousQuery<TBase, TReturn> Query;

        /// <summary>Creates a statement translator for the supplied root table.</summary>
        public SelectQueryBuilder(Table table)
        {
            Query = new SelectAnonymousQuery<TBase, TReturn>(table);
        }

        /// <summary>Projects a model member, optionally selecting specified nested members.</summary>
        public void Select(MemberInfo member, Expression expression)
        {
            Select(member, expression, null);
        }

        /// <summary>Projects a model member, optionally selecting specified nested members.</summary>
        public void Select(MemberInfo member, Expression expression, Expression[] sub_expressions)
        {
            var path = expression.GetMemberPath();
            var rel = Query.Table.FindRelationshipFromPath(path);
            if (rel != null)
            {
                // A projected relationship is rooted at the selected target, not
                // at intermediate ancestors, and each output alias owns its shape.
                Query.AddTableAsRoot();
                Query.Joins.AddTable(rel.ParentTable);
                var selected = new SelectRelationship(Query, rel, member.Name);
                Query.Selects.AddStatement(selected);
                selected.SubQuery.SelectRootColumns();
                if (sub_expressions == null) return;
                foreach (var child in sub_expressions)
                {
                    // Resolve relative to this selected object. Combining paths
                    // with Union removed repeated names such as Manager.Manager.
                    var childPath = child.GetMemberPath();
                    if (childPath.Count == 0) continue;
                    var childRel = rel.ChildTable.FindRelationshipFromPath(childPath);
                    if (childRel != null) selected.SubQuery.SelectRelationship(childRel);
                    else
                    {
                        var column = rel.ChildTable.FindColumnFromPath(childPath);
                        if (column == null) throw new SqlPathNotFoundException(rel.ChildTable, childPath);
                        selected.SubQuery.SelectColumn(column);
                    }
                }
            }
            else
            {
                var col = Query.Table.FindColumnFromPath(path);
                if (col == null)
                    throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Query.Table, path);
                Query.SelectColumn(col, member.Name, enable_sub_queries: false);
            }
        }

        /// <summary>Adds a CASE expression to the projection.</summary>
        public void SelectCase(MemberInfo member, LambdaExpression expression)
        {
            var case_statement = Case(member, expression);
            Query.Selects.AddStatement(case_statement);
        }


        /// <summary>Defines a CASE expression for the projected member.</summary>
        public CaseStatement Case(MemberInfo member, LambdaExpression expression)
        {
            var case_statement = new CaseStatement<TBase>(Query.Table, member.Name);
            expression.Compile().DynamicInvoke(case_statement);
            return case_statement;
        }

        /// <summary>Translates a function marker call into a SELECT expression.</summary>
        public SelectStatement GetSelectStatement(MemberInfo member, MethodCallExpression call_expression)
        {
            var args = new List<object>();
            args.Add(member);
            args.AddRange(ProcessArgs(call_expression.Arguments));
            var args_array = args.ToArray();

            // Keep the public void LEFT helper compatible while returning its
            // statement through expression dispatch, with a one-based start.
            if (call_expression.Method.Name == nameof(LEFT))
                return SUBSTRING(member, (Expression)args_array[1], Expression.Constant(1), (ConstantExpression)args_array[2]);

            var arg_types = args_array.Select(x => x.GetType()).ToArray();

            var method = this.GetType().GetMethod(call_expression.Method.Name, arg_types);
            if (method == null)
                throw new System.FormatException($"Invalid method target from expression '{call_expression}'.");
            return method.Invoke(this, args_array) as SelectStatement;
        }

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
    }
}
