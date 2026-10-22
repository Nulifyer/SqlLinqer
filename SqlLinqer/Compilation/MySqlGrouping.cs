using System.Collections.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Compilation
{
    internal static class MySqlGrouping
    {
        internal static string PrepareInputs(SelectQuery query, ParameterCollection parameters)
        {
            var joins = new List<string>();
            foreach (var statement in query.Selects.GetAll())
                if (!statement.DoGroupBy()) Visit(statement);
            return string.Join("", joins);

            void Visit(SelectStatement statement)
            {
                if (statement.DoGroupBy())
                {
                    // MySQL does not recognize a grouped computed expression
                    // inside a larger expression containing an aggregate.
                    if (!(statement is SelectFuncStatement) && !(statement is CaseStatement)) return;
                    if (parameters.FindExpressionOverride(statement) != null) return;
                    string value = statement.Render(query, parameters, DbFlavor.MySql, false, false);
                    string alias = "__sqlinqer_group" + joins.Count;
                    joins.Add($" CROSS JOIN LATERAL (SELECT {value} AS `Value`) AS `{alias}`");
                    parameters.OverrideExpression(statement, $"`{alias}`.`Value`");
                }
                else if (statement is SelectFuncStatement function)
                {
                    // Aggregate arguments are evaluated before grouping and must
                    // retain their original column/expression inputs.
                    if (!function.Func.DoGroupBy()) return;
                    foreach (var argument in function.Args)
                        if (argument is SelectStatement child) Visit(child);
                }
                else if (statement is CaseStatement conditional)
                {
                    foreach (var branch in conditional.Conditions)
                        if (branch.ReturnValue is SelectStatement child) Visit(child);
                }
            }
        }
    }
}
