using System.Linq;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Generic;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Components.Withs
{
    public class WithCollection : KeyedComponentCollectionBase<string, With>
    {
        public WithCollection() : base()
        {

        }

        public void AddWith(With with)
        {
            AddComponentOverwrite(with.Alias, with);
        }

        public string Render(JoinQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            if (Count == 0)
                return null;

            var strs = GetAll()
                .OrderBy(x => x.Order)
                .Select(x => x.Render(query, parameters, flavor));
            return $"WITH {string.Join(",", strs)}";
        }
    }
}