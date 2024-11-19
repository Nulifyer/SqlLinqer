using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    public class CaseStatement : SelectStatement
    {
        public List<CaseCondition> Conditions { get; protected set; }

        private readonly string _alias;
        public override string Alias => _alias;

        public CaseStatement(string alias) : base()
        {
            _alias = alias;
            Conditions = new List<CaseCondition>();
        }

        public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool for_json, bool include_alias)
        {
            var builder = new StringBuilder();
            builder.Append("case ");
            bool first = true;
            foreach (var con in Conditions)
            {
                if (first) first = false;
                else builder.Append(" ");
                con.Render(query, parameters, flavor, builder);
            }
            builder.Append(" end");
            string caseStr = builder.ToString();

            if (for_json && !query.IsSubQuery)
            {
                switch (flavor)
                {
                    case DbFlavor.PostgreSql:                        
                    case DbFlavor.MySql:
                        return $"{Alias.DbWrapString(flavor)}, ({caseStr})";
                    case DbFlavor.SqlServer:
                        string alias = include_alias ? $" AS {Alias.DbWrap(flavor)}" : null;
                        return $"({caseStr})" + alias;
                    default:
                        throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported by {nameof(CaseStatement)}.{nameof(Render)}");
                }
            }
            else
            {
                string alias = include_alias ? $" AS {Alias.DbWrap(flavor)}" : null;
                return $"({caseStr})" + alias;
            }
        }

        public override bool DoGroupBy() => true;

        public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            var cols = Conditions
                .SelectMany(x => x.Condition?.GetColumnReferences() ?? Array.Empty<Column>())
                .GroupBy(x => x.UUID)
                .Select(x => x.First())
                .Select(x => x.Render(flavor));
            return cols;
        }
    }
}