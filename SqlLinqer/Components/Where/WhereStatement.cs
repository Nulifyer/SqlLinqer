using System.Collections.Generic;
using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Components.Where
{
    public abstract class WhereStatement : IWhereComponent
    {
        public abstract int Order { get; }
        
        public abstract IWhereComponent Invert();
        public abstract IWhereComponent Finalize(WhereQuery query);
        public abstract void Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder);
        public abstract bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component);

        public abstract IEnumerable<Column> GetColumnReferences();
    }
}