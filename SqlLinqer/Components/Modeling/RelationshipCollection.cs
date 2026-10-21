using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Joins;

namespace SqlLinqer.Components.Modeling
{
    /// <summary>Indexes relationships by their mapped member aliases.</summary>
    public class RelationshipCollection : KeyedComponentCollectionBase<string, Relationship>
    {
        /// <summary>Creates an empty relationship collection.</summary>
        public RelationshipCollection() : base()
        {

        }

        /// <summary>Adds or replaces a relationship indexed by its data-target alias.</summary>
        public void AddRelationship(Relationship relationship)
        {
            AddComponentOverwrite(relationship.DataTarget.Alias, relationship);
        }
    }
}
