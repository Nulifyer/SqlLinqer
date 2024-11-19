using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Joins;

namespace SqlLinqer.Components.Modeling
{
    public class RelationshipCollection : KeyedComponentCollectionBase<string, Relationship>
    {
        public RelationshipCollection() : base()
        {
            
        }

        public void AddRelationship(Relationship relationship)
        {
            AddComponentOverwrite(relationship.DataTarget.Alias, relationship);
        }
    }
}