using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    /// <summary>A SQL CASE expression with an output alias.</summary>
    public class CaseStatement : SelectStatement
    {
        /// <summary>The CASE branches in evaluation order.</summary>
        public List<CaseCondition> Conditions { get; protected set; }

        private readonly string _alias;
        /// <inheritdoc/>
        public override string Alias => _alias;

        /// <summary>Creates a CASE expression with the supplied output alias.</summary>
        public CaseStatement(string alias) : base()
        {
            _alias = alias;
            Conditions = new List<CaseCondition>();
        }

        /// <inheritdoc/>
        public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool include_alias)
        {
            string caseStr = parameters.FindExpressionOverride(this) ?? parameters.FindExpression(this);
            if (caseStr == null)
            {
                var builder = new StringBuilder();
                builder.Append("(case ");
                bool first = true;
                foreach (var con in Conditions)
                {
                    if (first) first = false;
                    else builder.Append(" ");
                    con.Render(query, parameters, flavor, builder);
                }
                builder.Append(" end)");
                caseStr = builder.ToString();
                // Reuse both predicates and result bindings when this expression
                // appears again in GROUP BY or ORDER BY.
                parameters.RememberExpression(this, caseStr);
            }

            string alias = include_alias ? $" AS {Alias.DbWrap(flavor)}" : null;
            return caseStr + alias;
        }

        /// <inheritdoc/>
        public override bool DoGroupBy() => Conditions.All(x => !(x.ReturnValue is SelectStatement statement) || statement.DoGroupBy());

        /// <inheritdoc/>
        public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            if (DoGroupBy())
                return new[] { Render(query, parameters, flavor, false) };

            // An aggregate-bearing CASE cannot itself be a grouping expression.
            // Its predicates and non-aggregate branch inputs still need grouping.
            return Conditions
                .SelectMany(x => x.Condition?.GetColumnReferences() ?? Array.Empty<Column>())
                .Select(x => x.Render(flavor))
                .Concat(Conditions.SelectMany(x => x.ReturnValue is Column column ? new[] { column.Render(flavor) }
                    : x.ReturnValue is SelectStatement statement ? statement.RenderForGroupBy(query, parameters, flavor) ?? Array.Empty<string>()
                    : Array.Empty<string>()))
                .Distinct();
        }
    }
}
