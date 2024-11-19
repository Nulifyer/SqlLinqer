using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    public abstract class UpdateOperation
    {
        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="query">The query current query</param>
        /// <param name="statement">The parent statement containing the operation</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        public abstract string Render(UpdateQuery query, UpdateStatement statement, ParameterCollection parameters, DbFlavor flavor);
    }
}