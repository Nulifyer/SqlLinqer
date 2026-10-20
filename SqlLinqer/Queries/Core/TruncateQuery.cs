using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Core
{
    public class TruncateQuery : TableQuery
    {
        public TruncateQuery(Table table) : base(table)
        {
            
        }

        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            if (flavor == DbFlavor.Sqlite)
                throw new System.NotSupportedException("SQLite does not support TruncateQuery. Use DeleteQuery instead.");
            return $"TRUNCATE TABLE {Table.RenderFullName(flavor)}";
        }
    }
}