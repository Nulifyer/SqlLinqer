using System;
using System.Collections.Generic;
using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    /// <summary>A mapped column in a SELECT projection.</summary>
    public class SelectColumn : SelectStatement
    {
        private readonly string _alias;
        /// <summary>The mapped column to select.</summary>
        public readonly Column Column;
        /// <inheritdoc/>
        public override string Alias => _alias ?? Column.Alias;

        /// <summary>Creates a selected column, using the supplied alias when present.</summary>
        public SelectColumn(Column column)
        {
            Column = column;
        }
        /// <summary>Creates a selected column, using the supplied alias when present.</summary>
        public SelectColumn(Column column, string alias)
        {
            _alias = alias;
            Column = column;
        }

        /// <inheritdoc/>
        public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool include_alias)
        {
            query.Joins.AddTable(Column.Table);

            string alias = include_alias ? $" AS {Alias.DbWrap(flavor)}" : null;
            return Column.Render(flavor) + alias;
        }

        /// <inheritdoc/>
        public override bool DoGroupBy() => true;

        /// <inheritdoc/>
        public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
            => new[] { Render(query, parameters, flavor, false) };
    }
}
