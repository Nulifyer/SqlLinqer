using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Modeling
{
    public class ColumnCollection : KeyedComponentCollectionBase<string, Column>
    {
        public ColumnCollection() : base()
        {
            
        }

        public void AddColumn(Column column)
        {
            AddComponentOverwrite(column.Alias, column);
        }
    }
}