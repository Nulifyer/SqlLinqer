using System;
using System.Reflection;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Modeling
{
    /// <summary>Relationship attributes collected before resolving related table mappings.</summary>
    public class UnprocessedRelationshipMember
    {
        /// <summary>The model type declaring the relationship.</summary>
        public readonly Type ParentType;
        /// <summary>The related model type.</summary>
        public readonly Type ChildType;
        /// <summary>The member metadata for the relationship.</summary>
        public readonly UnprocessedColumnMember UnprocessedMember;
        /// <summary>The relationship cardinality and direction.</summary>
        public readonly RelationshipType RelationshipType;
        /// <summary>Automatic-operation settings for the relationship member.</summary>
        public readonly AutoOptions AutoOptions;
        /// <summary>The attribute used to resolve the relationship.</summary>
        public readonly RelationshipAttribute Info;

        internal UnprocessedRelationshipMember(Type parentType, MemberInfo member, RelationshipAttribute info)
        {
            ParentType = parentType;
            UnprocessedMember = new UnprocessedColumnMember(member, true);
            ChildType = UnprocessedMember.Member.ReflectedType;
            RelationshipType = info.RelationshipType;
            AutoOptions = new AutoOptions(member.ReflectedType, member, true);
            Info = info;
        }
    }
}