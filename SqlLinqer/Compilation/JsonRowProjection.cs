using System.Linq;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Compilation
{
    // Group and deduplicate SQL fields before JSON construction. MySQL cannot
    // infer computed grouping dependencies inside JSON_OBJECT, and both MySQL
    // and PostgreSQL require DISTINCT ordering expressions in the select list.
    internal static class JsonRowProjection
    {
        private const string RowsAlias = "__sqlinqer_rows";

        internal static void PrepareOrdering(SelectQuery query, RenderContext context, ref string select,
            ref string innerOrder, out string outerOrder)
        {
            outerOrder = null;
            var orders = query.OrderBys.Statements.ToArray();
            if (orders.Length == 0) return;
            var flavor = context.Dialect.Flavor;
            string rows = RowsAlias.DbWrap(flavor);
            if (orders.All(x => x is ColumnOrderBy column && query.Selects.TryGet(column.Column.Alias) != null))
            {
                outerOrder = "ORDER BY " + string.Join(",", orders.Cast<ColumnOrderBy>()
                    .Select(x => $"{rows}.{x.Column.Alias.DbWrap(flavor)} {x.Dir.GetDescription()}"));
                innerOrder = null;
            }
            else if (!query.Distinct)
            {
                // A window ordinal preserves arbitrary sorting expressions without
                // exposing extra JSON members or relying on derived-table order.
                string alias = "__sqlinqer_order";
                while (query.Selects.TryGet(alias) != null) alias += "_";
                select += $",ROW_NUMBER() OVER ({innerOrder}) AS {alias.DbWrap(flavor)}";
                outerOrder = $"ORDER BY {rows}.{alias.DbWrap(flavor)}";
                innerOrder = null;
            }
            // Unselected DISTINCT ordering expressions retain provider validation.
        }

        internal static PreparedSelect Wrap(SelectQuery query, RenderContext context, string select,
            string from, string where, string group, string innerOrder, string outerOrder, string with)
        {
            var dialect = context.Dialect;
            var flavor = dialect.Flavor;
            var inner = new PreparedSelect(null, select, from, where, group, innerOrder, query.Distinct, false,
                false, false, 0, 0, 0);
            string rows = RowsAlias.DbWrap(flavor);
            string fields = string.Join(",", query.Selects.GetAll().Select(x => $"{x.Alias.DbWrapString(flavor)}, {rows}.{x.Alias.DbWrap(flavor)}"));
            bool collection = query.IsSubQuery && query.Table.ParentRelationship?.Type != RelationshipType.OneToOne;
            string source = $"FROM ({dialect.Render(inner)}) AS {rows}";

            // Page logical rows before aggregating a nested collection into JSON.
            if (collection)
            {
                var page = new PreparedSelect(null, $"{rows}.*", source, null, null, outerOrder, false, false,
                    false, false, query.PagingControls.Top, query.PagingControls.Limit, query.PagingControls.Offset);
                source = $"FROM ({dialect.Render(page)}) AS {rows}";
            }
            return new PreparedSelect(with, dialect.JsonProjection(fields, false, collection), source, null, null,
                collection ? null : outerOrder, false, true, query.IsSubQuery, collection,
                collection ? 0 : query.PagingControls.Top, collection ? 0 : query.PagingControls.Limit,
                collection ? 0 : query.PagingControls.Offset);
        }
    }
}
