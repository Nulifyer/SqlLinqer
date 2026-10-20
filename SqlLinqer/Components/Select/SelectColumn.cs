using System;
using System.Collections.Generic;
using System.Linq;
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

            if (forJson && flavor == DbFlavor.Sqlite
                && (Column.DbType == System.Data.DbType.Binary || Column.Table.Columns.GetAll<ReflectedColumn>()
                    .Any(x => x.ColumnName == Column.ColumnName && x.ValueType == typeof(byte[]))))
            {
                string value = Column.Render(flavor);
                return $"{Alias.DbWrapString(flavor)}, CASE WHEN {value} IS NULL THEN NULL ELSE hex({value}) END";
            }

            if (forJson)
            {
                switch (flavor)
                {
                    case DbFlavor.Sqlite:
                    case DbFlavor.DuckDb:
                    case DbFlavor.PostgreSql:
                    case DbFlavor.MySql:
                        string value = Column.Render(flavor);
                        if (flavor == DbFlavor.DuckDb && Column is ReflectedColumn reflected && reflected.ValueType == typeof(byte[]))
                            value = $"to_base64({value})";
                        return $"{Alias.DbWrapString(flavor)}, {value}";
                    case DbFlavor.SqlServer:
                    case DbFlavor.AzureSql:
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
