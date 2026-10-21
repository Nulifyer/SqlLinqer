using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Components.Outputs
{
    /// <summary>
    /// An output column statement
    /// </summary>
    public class ColumnOutput : IOutputStatement
    {
        /// <summary>The SQL Server output table alias, such as inserted or deleted.</summary>
        public readonly string PseudoTable;
        /// <summary>The modified column to return.</summary>
        public readonly Column Column;

        /// <summary>Creates a modified-column output expression.</summary>
        public ColumnOutput(Column column, string pseudo_table)
        {
            PseudoTable = pseudo_table;
            Column = column;
        }

        /// <inheritdoc/>
        public string Render(DbFlavor flavor)
        {
            return Render(flavor, null);
        }

        internal string Render(DbFlavor flavor, string targetAlias)
        {
            switch (flavor)
            {
                case DbFlavor.SqlServer:
                case DbFlavor.AzureSql:
                    return $"{PseudoTable.DbWrap(flavor)}.{Column.ColumnName.DbWrap(flavor)}";
                case DbFlavor.Sqlite:
                    return Column.ColumnName.DbWrap(flavor);
                case DbFlavor.DuckDb:
                case DbFlavor.PostgreSql:
                    return targetAlias == null
                        ? Column.ColumnName.DbWrap(flavor)
                        : $"{targetAlias.DbWrap(flavor)}.{Column.ColumnName.DbWrap(flavor)}";
                default:
                    throw new System.NotSupportedException($"Returning modified rows is not supported for {flavor}.");
            }
        }
    }
}
