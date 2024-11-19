using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Extensions.StringExtensions;
using System;

namespace SqlLinqer.Components.Ordering
{
    public class ColumnOrderBy : IOrderByStatement
    {
        public Column Column;
        public SqlDir Dir;

        public ColumnOrderBy(Column column, SqlDir dir)
        {
            Column = column;
            Dir = dir;
        }

        public string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            switch (flavor)
            {
                case DbFlavor.PostgreSql:
                case DbFlavor.MySql:
                    var selectStatement = query.Selects.TryGet(Column.Alias);
                    string staement = selectStatement.Render(query, parameters, flavor, false, false);
                    return $"{staement} {Dir.GetDescription()}";
                case DbFlavor.SqlServer:
                    return $"{Column.Alias.DbWrap(flavor)} {Dir.GetDescription()}";
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported by {nameof(ColumnOrderBy)}.{nameof(Render)}");
            }
        }
    }
}