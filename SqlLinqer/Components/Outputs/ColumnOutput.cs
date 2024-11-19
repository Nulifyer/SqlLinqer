using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Components.Outputs
{
    /// <summary>
    /// An output column statement
    /// </summary>
    public class ColumnOutput : IOutputStatement
    {
        public readonly string PseudoTable;
        public readonly Column Column;

        public ColumnOutput(Column column, string pseudo_table)
        {
            PseudoTable = pseudo_table;
            Column = column;
        }

        /// <inheritdoc/>
        public string Render(DbFlavor flavor)
        {
            return $"{PseudoTable.DbWrap(flavor)}.{Column.ColumnName.DbWrap(flavor)}";
        }
    }
}