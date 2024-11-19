using SqlLinqer.Components.Render;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// An update statement
    /// </summary>
    public abstract class UpdateStatement
    {
        /// <summary>
        /// Value Column
        /// </summary>
        public Column UpdateColumn { get; protected set;}

        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="query">The query current query</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        public abstract string Render(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor);
    }
}