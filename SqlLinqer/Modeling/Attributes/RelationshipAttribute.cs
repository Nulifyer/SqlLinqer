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
        /// <summary>The relationship cardinality and direction described by this attribute.</summary>
        public readonly RelationshipType RelationshipType;

        /// <summary>Creates a relationship attribute with the supplied cardinality and direction.</summary>
        public RelationshipAttribute(RelationshipType relationshipType)
        {
            RelationshipType = relationshipType;
        }

        /// <summary>Resolves the relationship attribute into mapped columns and joins.</summary>
        public abstract Relationship ProcessRelationship(Table currentTable, UnprocessedRelationshipMember unprocessed);
    }
}
