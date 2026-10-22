using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Components.Select;
using SqlLinqer.Extensions.StringExtensions;
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
            bool projectCollection = query.IsSubQuery && query.Table.ParentRelationship?.Type != RelationshipType.OneToOne
                && (query.OrderBys.Count > 0 || query.PagingControls.Top > 0 || query.PagingControls.Limit > 0);
            bool projectRows = forJson && ((flavor == DbFlavor.MySql && query.Selects.GetAll().Any(x => !x.DoGroupBy()))
                || ((query.Distinct || projectCollection) && (flavor == DbFlavor.MySql || flavor == DbFlavor.PostgreSql)));
            string where = query.RootWhere.Render(query, parameters, flavor);
            string groupingInputs = flavor == DbFlavor.MySql ? MySqlGrouping.PrepareInputs(query, parameters) : null;
            string select = projectRows
                ? string.Join(",", query.Selects.GetAll().Select(x =>
                {
                    string value = x.RenderJsonValue(query, parameters, flavor);
                    if (query.Distinct && flavor == DbFlavor.PostgreSql && x is SelectRelationship)
                        value = $"CAST({value} AS jsonb)";
                    return $"{value} AS {x.Alias.DbWrap(flavor)}";
                }))
                : query.Selects.Render(query, parameters, flavor, forJson);
            string group = query.Selects.RenderGroupBy(query, parameters, flavor);
            string order = query.OrderBys.Render(query, parameters, flavor);
            string outerOrder = null;
            if (projectRows)
                JsonRowProjection.PrepareOrdering(query, context, ref select, ref order, out outerOrder);
            string with = query.IsSubQuery ? null : query.Withs?.Render(query, parameters, flavor);
            string from = query.Joins.Render(query, parameters, flavor) + groupingInputs;
            if (projectRows)
                return JsonRowProjection.Wrap(query, context, select, from, where, group, order, outerOrder, with);
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
