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

            if (query.IsPredicateSubQuery)
                RemoveOrderOnlySelections(query);

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
            query.Joins.RootHint = query.RowLock == null ? null : dialect.RowLockHint(query.RowLock);
            string from = query.Joins.Render(query, parameters, flavor) + groupingInputs;
            string rowLock = null;
            if (query.RowLock != null)
            {
                if (query.Distinct || !string.IsNullOrEmpty(group) && query.Selects.GetAll().Any(x => !x.DoGroupBy()))
                    throw new System.NotSupportedException("A row lock cannot be combined with DISTINCT or aggregates.");
                rowLock = dialect.RowLockClause(query.RowLock, query.Table.Alias.DbWrap(flavor), query.Joins.HasJoinedTables(query.Table));
            }
            return new PreparedSelect(with, select, from, where, group, order, query.Distinct,
                query.PagingControls.Top, query.PagingControls.Limit, query.PagingControls.Offset, rowLock);
        }

        // Ordering by an unselected column adds that column to the select list under a generated alias.
        // A subselect compared with a column may return only one column, so it orders by the source column instead.
        private static void RemoveOrderOnlySelections(SelectQuery query)
        {
            var selections = query.Selects.GetAll().ToList();
            var kept = selections.Where(x => !(x is SelectColumn column && column.Alias == column.Column.UUID)).ToList();
            if (kept.Count == selections.Count || kept.Count == 0) return;
            query.Selects.Clear();
            foreach (var selection in kept) query.Selects.AddStatement(selection);
        }

        // Answers whether any row matches the query's filters. Selection, ordering, paging, and row locks do not apply.
        internal static string Exists(SelectQuery query, RenderContext context)
        {
            var parameters = context.Parameters;
            var flavor = context.Dialect.Flavor;
            query.PagingControls.Reset();
            query.OrderBys.Clear();
            query.Selects.Clear();
            query.RowLock = null;
            query.Joins.AddRootTable(query.Table);
            string where = query.RootWhere.Render(query, parameters, flavor);
            string from = query.Joins.Render(query, parameters, flavor);
            string with = query.Withs?.Render(query, parameters, flavor);
            return string.Join(" ", new[] { with, "SELECT CASE WHEN EXISTS (SELECT 1", from, where }.Where(x => !string.IsNullOrEmpty(x)))
                + ") THEN 1 ELSE 0 END";
        }

        internal static string Count(SelectQuery query, RenderContext context)
        {
            query.PagingControls.Reset();
            query.OrderBys.Clear();
            query.RowLock = null;
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
