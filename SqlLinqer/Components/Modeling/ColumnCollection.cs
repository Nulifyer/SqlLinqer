using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Modeling
{
    /// <summary>Indexes mapped columns by their aliases.</summary>
    public class ColumnCollection : KeyedComponentCollectionBase<string, Column>
    {
        /// <summary>Creates an empty column collection.</summary>
        public ColumnCollection() : base()
        {

        }

        /// <summary>Adds or replaces a column indexed by its alias.</summary>
        public void AddColumn(Column column)
        {
            AddComponentOverwrite(column.Alias, column);
        }
    }
}
