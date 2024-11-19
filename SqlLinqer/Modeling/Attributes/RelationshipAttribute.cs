using System;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Used to label one to one realtionships
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public abstract class RelationshipAttribute : Attribute
    {
        public readonly RelationshipType RelationshipType;

        public RelationshipAttribute(RelationshipType relationshipType)
        {
            RelationshipType = relationshipType;
        }
        
        public abstract Relationship ProcessRelationship(Table currentTable, UnprocessedRelationshipMember unprocessed);
    }
}
