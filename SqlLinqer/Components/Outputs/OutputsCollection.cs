using System.Linq;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Outputs
{
    /// <summary>
    /// A collection of output statements
    /// </summary>
    public class OutputsCollection : ComponentCollectionBase<IOutputStatement>
    {
        public void AddOutput(IOutputStatement statement)
        {
            AddComponent(statement);
        }

        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        public string Render(DbFlavor flavor)
        {
            return Render(flavor, null);
        }

        internal string Render(DbFlavor flavor, string targetAlias)
        {
            if (Count == 0)
                return null;

            string keyword;
            switch (flavor)
            {
                case DbFlavor.SqlServer:
                case DbFlavor.AzureSql:
                    keyword = "OUTPUT";
                    break;
                case DbFlavor.Sqlite:
                case DbFlavor.DuckDb:
                case DbFlavor.PostgreSql:
                    keyword = "RETURNING";
                    break;
                default:
                    throw new System.NotSupportedException($"Returning modified rows is not supported for {flavor}. Use a query without output columns.");
            }

            var strs = GetAll().Select(x => x is ColumnOutput column ? column.Render(flavor, targetAlias) : x.Render(flavor));
            return $"{keyword} {string.Join(",", strs)}";
        }
    }
}
