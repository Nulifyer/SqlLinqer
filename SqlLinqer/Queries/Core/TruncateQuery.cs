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
            return $"TRUNCATE TABLE {Table.RenderFullName(flavor)}";
        }
    }
}