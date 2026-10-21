using System.Linq;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>Indexes update assignments by their target column identities.</summary>
    public class UpdatesCollection : KeyedComponentCollectionBase<string, UpdateStatement>
    {
        /// <summary>Adds or replaces an assignment for the same target column.</summary>
        public void AddUpdate(UpdateStatement statement)
        {
            AddComponentOverwrite(statement.UpdateColumn.UUID, statement);
        }

        /// <summary>Renders this component for the selected database dialect.</summary>
        public string Render(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            if (Count == 0)
                return null;

            return $"UPDATE {query.Table.Alias.DbWrap(flavor)} SET {RenderAssignments(query, parameters, flavor)}";
        }

        internal string RenderAssignments(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            return string.Join(",", GetAll().Select(x => x.Render(query, parameters, flavor)));
        }
    }
}
