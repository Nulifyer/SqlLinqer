using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Extensions.StringExtensions;
using System;

namespace SqlLinqer.Components.Ordering
{
    /// <summary>Orders results by a mapped column.</summary>
    public class ColumnOrderBy : IOrderByStatement
    {
        /// <summary>The column used for sorting.</summary>
        public Column Column;
        /// <summary>The sorting direction.</summary>
        public SqlDir Dir;

        /// <summary>Creates a column sorting expression.</summary>
        public ColumnOrderBy(Column column, SqlDir dir)
        {
            Column = column;
            Dir = dir;
        }

        /// <summary>Renders this component for the selected database dialect.</summary>
        public string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            switch (flavor)
            {
                case DbFlavor.Sqlite:
                    // Anonymous result columns name selected expressions, not source tables.
                    if (Column.Table.UUID != query.Table.UUID && !Column.Table.IsChildOf(query.Table))
                    {
                        var selected = query.Selects.TryGet(Column.Alias);
                        if (selected == null)
                            throw new InvalidOperationException($"No selected expression matches ordering column '{Column.Alias}'.");
                        return $"{selected.Render(query, parameters, flavor, false)} {Dir.GetDescription()}";
                    }
                    query.Joins.AddTable(Column.Table);
                    return $"{Column.Render(flavor)} {Dir.GetDescription()}";
                case DbFlavor.DuckDb:
                case DbFlavor.PostgreSql:
                case DbFlavor.MySql:
                    var selectStatement = query.Selects.TryGet(Column.Alias);
                    string staement = flavor == DbFlavor.DuckDb
                        ? parameters.FindExpression(selectStatement) ?? selectStatement.Render(query, parameters, flavor, false)
                        : selectStatement.Render(query, parameters, flavor, false);
                    return $"{staement} {Dir.GetDescription()}";
                case DbFlavor.SqlServer:
                case DbFlavor.AzureSql:
                    return $"{Column.Alias.DbWrap(flavor)} {Dir.GetDescription()}";
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported by {nameof(ColumnOrderBy)}.{nameof(Render)}");
            }
        }
    }
}
