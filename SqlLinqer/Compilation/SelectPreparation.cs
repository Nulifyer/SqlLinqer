using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Compilation
{
    internal static class SelectPreparation
    {
        internal static PreparedSelect Prepare(SelectQuery query, RenderContext context, bool countGroups = false)
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
            string groupingInputs = flavor == DbFlavor.MySql ? MySqlGrouping.PrepareInputs(query, parameters) : null;
            string group = null;
            if (countGroups)
            {
                // Measures may discover joins that affect group membership, but
                // their values and private bindings are not needed to count groups.
                query.Selects.Render(query, new ParameterCollection(flavor), flavor);
                group = query.Selects.RenderGroupBy(query, parameters, flavor);
            }
            bool omitMeasures = countGroups && !string.IsNullOrEmpty(group);
            string select = omitMeasures ? "1 AS " + "__sqlinqer_group".DbWrap(flavor)
                : query.Selects.Render(query, parameters, flavor);
            group = group ?? query.Selects.RenderGroupBy(query, parameters, flavor);
            string order = query.OrderBys.Render(query, parameters, flavor);
            string with = query.IsSubQuery ? null : query.Withs?.Render(query, parameters, flavor);
            string from = query.Joins.Render(query, parameters, flavor) + groupingInputs;
            return new PreparedSelect(with, select, from, where, group, order, query.Distinct,
                query.PagingControls.Top, query.PagingControls.Limit, query.PagingControls.Offset);
        }

        internal static string Count(SelectQuery query, RenderContext context)
        {
            query.PagingControls.Reset();
            query.OrderBys.Clear();
            bool wasSubQuery = query.IsSubQuery;
            if (context.Dialect.Capabilities.HoistCountWiths)
                query.IsSubQuery = true;
            bool aggregate = query.Selects.GetAll().Any(statement => !statement.DoGroupBy());
            // DISTINCT needs projected values. An aggregate without dimensions
            // must retain its one output row even when its input is empty.
            bool countGroups = aggregate && !query.Distinct;
            var inner = Prepare(query, context, countGroups);
            string with = context.Dialect.Capabilities.HoistCountWiths && !wasSubQuery
                ? query.Withs?.Render(query, context.Parameters, context.Dialect.Flavor) : null;
            return context.Dialect.RenderCount(inner, with);
        }
    }
}
