using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Core
{
    /// <summary>Builds a TRUNCATE command for a mapped table.</summary>
    public class TruncateQuery : TableQuery
    {
        /// <summary>Creates a TRUNCATE query for the supplied table.</summary>
        public TruncateQuery(Table table) : base(table)
        {

        }

        /// <inheritdoc/>
        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            var working = IsCompilationCopy ? this : SqlLinqer.Compilation.SnapshotGraph.Capture(this);
            return working.RenderCore(parameters, flavor);
        }

        private string RenderCore(ParameterCollection parameters, DbFlavor flavor)
        {
            if (flavor == DbFlavor.Sqlite)
                throw new System.NotSupportedException("SQLite does not support TruncateQuery. Use DeleteQuery instead.");
            return $"TRUNCATE TABLE {Table.RenderFullName(flavor)}";
        }
    }
}
