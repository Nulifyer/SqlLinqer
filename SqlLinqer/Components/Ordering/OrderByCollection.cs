using System.Linq;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Ordering
{
    public class OrderByCollection : ComponentCollectionBase<IOrderByStatement>
    {
        public void AddStatement(IOrderByStatement statement)
            => base.AddComponent(statement);
        public void Clear()
            => base.Clear();
        public string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            if (Count == 0)
                return null;

            var strs = GetAll().Select(x => x.Render(query, parameters, flavor));
            return $"ORDER BY {string.Join(",", strs)}";
        }
    }
}