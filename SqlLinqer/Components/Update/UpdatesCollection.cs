using System.Linq;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    public class UpdatesCollection : KeyedComponentCollectionBase<string, UpdateStatement>
    {
        public void AddUpdate(UpdateStatement statement)
        {
            AddComponentOverwrite(statement.UpdateColumn.UUID, statement);
        }

        public string Render(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            if (Count == 0)
                return null;

            var strs = GetAll().Select(x => x.Render(query, parameters, flavor));
            return $"UPDATE {query.Table.Alias.DbWrap(flavor)} SET {string.Join(",", strs)}";
        }
    }
}