using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Ordering
{
    // SQL Server paging must order without adding columns to DISTINCT or GROUP BY.
    internal sealed class ProjectionOrderBy : IOrderByStatement
    {
        private readonly string _alias;
        internal string Alias => _alias;
        internal ProjectionOrderBy(string alias) { _alias = alias; }

        public string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
            => _alias.DbWrap(flavor) + " ASC";
    }
}
