using System.Linq;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Outputs
{
    /// <summary>
    /// A collection of output statements
    /// </summary>
    public class OutputsCollection : ComponentCollectionBase<IOutputStatement>
    {
        public void AddOutput(IOutputStatement statement)
        {
            AddComponent(statement);
        }

        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        public string Render(DbFlavor flavor)
        {
            if (Count == 0)
                return null;

            var strs = GetAll().Select(x => x.Render(flavor));
            return $"OUTPUT {string.Join(",", strs)}";
        }
    }
}