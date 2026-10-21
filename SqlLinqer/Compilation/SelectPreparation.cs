using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Compilation
{
    internal static class SelectPreparation
    {
        internal static PreparedSelect Prepare(SelectQuery query, RenderContext context, bool forJson)
        {
            var dialect = context.Dialect;
            var parameters = context.Parameters;
            var flavor = dialect.Flavor;
            query.Joins.AddRootTable(query.Table);
            if (query.PagingControls.Limit > 0 && query.OrderBys.Count == 0)
            {
                bool preserveProjection = query.Distinct || query.Selects.GetAll().Any(x => !x.DoGroupBy());
                if (!preserveProjection)
                {
                    var column = query.Table.PrimaryKey ?? query.Table.Columns.GetAll().First();
                    query.OrderBy(column, SqlDir.ASC, true, dialect.Capabilities.OrderingNeedsSelectedColumn);
                }
                else if (dialect.Capabilities.RequiresOrderForOffset && query.Selects.Count > 0)
                    query.OrderBys.AddStatement(new ProjectionOrderBy(query.Selects.GetAll().First().Alias));
            }

            // Resolve every expression that can discover joins or CTEs before assembling
            // WITH and FROM. These mutations affect only this compilation's graph.
            string where = query.RootWhere.Render(query, parameters, flavor);
            string select = query.Selects.Render(query, parameters, flavor, forJson);
            string group = query.Selects.RenderGroupBy(query, parameters, flavor);
            string order = query.OrderBys.Render(query, parameters, flavor);
            string with = query.IsSubQuery ? null : query.Withs?.Render(query, parameters, flavor);
            string from = query.Joins.Render(query, parameters, flavor);
            return new PreparedSelect(with, select, from, where, group, order, query.Distinct, forJson,
                query.IsSubQuery, query.Table.ParentRelationship?.Type != RelationshipType.OneToOne,
                query.PagingControls.Top, query.PagingControls.Limit, query.PagingControls.Offset);
        }

        internal static string Count(SelectQuery query, RenderContext context)
        {
            query.PagingControls.Reset();
            query.OrderBys.Clear();
            bool wasSubQuery = query.IsSubQuery;
            if (context.Dialect.Capabilities.HoistCountWiths)
                query.IsSubQuery = true;
            var inner = Prepare(query, context, !context.Dialect.Capabilities.HoistCountWiths);
            string with = context.Dialect.Capabilities.HoistCountWiths && !wasSubQuery
                ? query.Withs?.Render(query, context.Parameters, context.Dialect.Flavor) : null;
            return context.Dialect.RenderCount(inner, with);
        }
    }
}
