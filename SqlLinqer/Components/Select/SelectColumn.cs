using System;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    public class SelectColumn : SelectStatement
    {
        private readonly string _alias;
        public readonly Column Column;
        public override string Alias => _alias ?? Column.Alias;

        public SelectColumn(Column column)
        {
            Column = column;
        }
        public SelectColumn(Column column, string alias)
        {
            _alias = alias;
            Column = column;
        }

        /// <inheritdoc/>
        public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool forJson, bool include_alias)
        {
            query.Joins.AddTable(Column.Table);

            if (forJson)
            {
                switch (flavor)
                {
                    case DbFlavor.PostgreSql:
                    case DbFlavor.MySql:
                        return $"{Alias.DbWrapString(flavor)}, {Column.Render(flavor)}";
                    case DbFlavor.SqlServer:
                        string alias = include_alias ? $" AS {Alias.DbWrap(flavor)}" : null;
                        return Column.Render(flavor) + alias;
                    default:
                        throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported by {nameof(SelectColumn)}.{nameof(Render)}");
                }
            }
            else
            {
                string alias = include_alias ? $" AS {Alias.DbWrap(flavor)}" : null;
                return Column.Render(flavor) + alias;
            }
        }

        /// <inheritdoc/>
        public override bool DoGroupBy() => true;

        /// <inheritdoc/>
        public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
            => new[] { Render(query, parameters, flavor, false, false) };
    }
}