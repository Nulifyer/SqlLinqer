using System;
using System.Reflection;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Modeling
{
    public class UnprocessedRelationshipMember
    {
        public readonly Type ParentType;
        public readonly Type ChildType;
        public readonly UnprocessedColumnMember UnprocessedMember;
        public readonly RelationshipType RelationshipType;
        public readonly AutoOptions AutoOptions;
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