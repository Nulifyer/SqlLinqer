using System;
using System.Linq;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    public class SelectCollection : KeyedComponentCollectionBase<string, SelectStatement>
    {
        public void AddStatement(SelectStatement statement)
        {
            AddComponentOverwrite(statement.Alias, statement);
        }

        public void Clear()
            => base.Clear();

        public string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool forJson)
        {
            if (Count == 0)
                return null;

            var strs = GetAll().Select(x => x.Render(query, parameters, flavor, forJson, true));
            
            string fields = string.Join(",", strs);
            return forJson ? SqlLinqer.Compilation.SqlDialect.For(flavor).JsonProjection(fields, query.Distinct,
                query.IsSubQuery && query.Table.ParentRelationship?.Type != Modeling.RelationshipType.OneToOne) : fields;
        }

        protected internal string RenderGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            var statements = GetAll();
            var group_by_statements = statements.Where(x => x.DoGroupBy());

            // there are no non-group-by statements
            if (group_by_statements.Count() == statements.Count())
                return null;

            var strs = group_by_statements
                .SelectMany(x => x.RenderForGroupBy(query, parameters, flavor))
                .Where(x => x != null)
                .Distinct();

            if (strs.Count() == 0)
                return null;
            else
                return $"GROUP BY {string.Join(",", strs)}";
        }
    }
}
